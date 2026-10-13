using System.Diagnostics;
using System.Text;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Owns one Bash process and keeps a bounded, session-local output buffer.
/// </summary>
internal sealed class BackgroundBashTask
{
    private readonly object _gate = new();
    private readonly Process _process;
    private readonly int? _timeoutSeconds;
    private readonly StringBuilder _output = new();
    private readonly DateTimeOffset _startedUtc;
    private Task _runTask = Task.CompletedTask;
    private BackgroundTaskStatus _status = BackgroundTaskStatus.Starting;
    private DateTimeOffset? _finishedUtc;
    private int? _exitCode;
    private int _logStartOffset;
    private bool _stopRequested;
    private string? _startError;

    public BackgroundBashTask(
        string taskId,
        string command,
        string workingDirectory,
        int? timeoutSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        _timeoutSeconds = timeoutSeconds;
        TaskId = taskId;
        Command = command;
        WorkingDirectory = workingDirectory;
        _startedUtc = DateTimeOffset.UtcNow;
        _process = new Process
        {
            StartInfo = BashProcess.CreateStartInfo(
                workingDirectory,
                command,
                redirectStandardInput: true)
        };
    }

    public string TaskId { get; }

    public string Command { get; }

    public string WorkingDirectory { get; }

    public bool Start()
    {
        try
        {
            if (!_process.Start())
            {
                _startError = "The shell process failed to start.";
                SetTerminalState(BackgroundTaskStatus.Failed, null);
                _process.Dispose();
                return false;
            }

            _process.StandardInput.Close();

            lock (_gate)
            {
                _status = BackgroundTaskStatus.Running;
            }

            _runTask = ObserveAsync();
            return true;
        }
        catch (Exception exception)
        {
            _startError = "The shell process failed to start: " + exception.Message;
            BashProcess.TryKill(_process);
            SetTerminalState(BackgroundTaskStatus.Failed, null);
            _process.Dispose();
            return false;
        }
    }

    public string StartError => _startError ?? "The background shell process failed to start.";

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _status is BackgroundTaskStatus.Starting or BackgroundTaskStatus.Running;
            }
        }
    }

    public BackgroundTaskSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new BackgroundTaskSnapshot(
                TaskId,
                Command,
                WorkingDirectory,
                _status,
                _exitCode,
                _startedUtc,
                _finishedUtc,
                _logStartOffset,
                _logStartOffset + _output.Length);
        }
    }

    public BackgroundTaskLog ReadLogs(int offset, int limit)
    {
        lock (_gate)
        {
            var logEndOffset = _logStartOffset + _output.Length;
            if (offset < _logStartOffset)
            {
                return new BackgroundTaskLog(
                    "The requested log offset is no longer available.",
                    _logStartOffset,
                    logEndOffset > _logStartOffset,
                    true);
            }

            var start = Math.Min(offset, logEndOffset);
            var length = Math.Min(limit, logEndOffset - start);
            var text = _output.ToString(start - _logStartOffset, length);
            var nextOffset = start + length;
            return new BackgroundTaskLog(text, nextOffset, nextOffset < logEndOffset, false);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_status is not (BackgroundTaskStatus.Starting or BackgroundTaskStatus.Running))
            {
                return;
            }

            _stopRequested = true;
        }

        BashProcess.TryKill(_process);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stop();
        await _runTask.WaitAsync(cancellationToken);
    }

    private async Task ObserveAsync()
    {
        using var timeout = new CancellationTokenSource();
        if (_timeoutSeconds is int seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        var stdout = ReadStreamAsync(_process.StandardOutput, timeout.Token);
        var stderr = ReadStreamAsync(_process.StandardError, timeout.Token);
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            SetTerminalState(
                _process.ExitCode == 0
                    ? BackgroundTaskStatus.Exited
                    : BackgroundTaskStatus.Failed,
                _process.ExitCode);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            BashProcess.TryKill(_process);
            SetTerminalState(
                WasStopped()
                    ? BackgroundTaskStatus.Stopped
                    : BackgroundTaskStatus.TimedOut,
                null);
        }
        catch (Exception exception)
        {
            BashProcess.TryKill(_process);
            AppendOutput("The background shell task failed: " + exception.Message);
            SetTerminalState(BackgroundTaskStatus.Failed, null);
        }
        finally
        {
            if (!BashProcess.HasExited(_process))
            {
                BashProcess.TryKill(_process);
            }

            try
            {
                await Task.WhenAll(stdout, stderr);
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or IOException
                or ObjectDisposedException)
            {
            }

            _process.Dispose();
        }
    }

    private async Task ReadStreamAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return;
            }

            AppendOutput(new string(buffer, 0, count));
        }
    }

    private void AppendOutput(string text)
    {
        lock (_gate)
        {
            var maximum = WorkspaceLimits.MaximumBackgroundTaskOutputCharacters;
            if (text.Length >= maximum)
            {
                var discarded = _output.Length + text.Length - maximum;
                _output.Clear();
                _logStartOffset += discarded;
                _output.Append(text.AsSpan(text.Length - maximum));
                return;
            }

            var overflow = _output.Length + text.Length - maximum;
            if (overflow > 0)
            {
                _output.Remove(0, overflow);
                _logStartOffset += overflow;
            }

            _output.Append(text);
        }
    }

    private bool WasStopped()
    {
        lock (_gate)
        {
            return _stopRequested;
        }
    }

    private void SetTerminalState(BackgroundTaskStatus status, int? exitCode)
    {
        lock (_gate)
        {
            if (_status is not (BackgroundTaskStatus.Starting or BackgroundTaskStatus.Running))
            {
                return;
            }

            _status = _stopRequested
                ? BackgroundTaskStatus.Stopped
                : status;
            _exitCode = exitCode;
            _finishedUtc = DateTimeOffset.UtcNow;
        }
    }
}
