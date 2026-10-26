using System.Diagnostics;
using System.Text;
using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Notifications;

/// <summary>
/// Runs the operator's notification command when the session waits for the
/// operator. The command never blocks the prompt that triggered it. Output
/// is drained and discarded. The first failure in a session is reported once.
/// </summary>
internal sealed class OperatorNotifier
{
    public const string Title = "CrystalCode";

    private const int MaximumFieldCharacters = 500;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<OperatorNotifySettings> _settings;
    private readonly Func<string> _workspace;
    private readonly Func<string> _session;
    private readonly Action<string> _note;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly HashSet<Task> _running = [];
    private int _failureReported;

    public OperatorNotifier(
        Func<OperatorNotifySettings> settings,
        Func<string> workspace,
        Func<string> session,
        Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(note);
        _settings = settings;
        _workspace = workspace;
        _session = session;
        _note = note;
    }

    /// <summary>
    /// Schedules the command for <paramref name="notice"/> when its event is
    /// subscribed. Disposing the result before the configured delay passes
    /// cancels the notification. A command that already started keeps running
    /// until it exits or times out.
    /// </summary>
    public IDisposable Begin(OperatorNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var settings = _settings();
        if (!settings.Sends(notice.Event) || _stop.IsCancellationRequested)
        {
            return Pending.None;
        }

        var pending = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var token = pending.Token;
        var work = Task.Run(() => DeliverAsync(settings, notice, token));
        Track(work);
        return new Pending(pending);
    }

    /// <summary>
    /// Runs the configured command now, whether or not notification is
    /// enabled, and returns the failure text or null when it exited with code 0.
    /// </summary>
    public async Task<string?> TestAsync(CancellationToken cancellationToken)
    {
        var settings = _settings();
        if (!settings.HasCommand)
        {
            return "No operator notification command is configured.";
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        var notice = new OperatorNotice(OperatorNotifyEvent.Test, "Test notification.", []);
        return await RunAsync(settings, notice, linked.Token);
    }

    /// <summary>Stops pending notifications and running commands.</summary>
    public void Cancel()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }
    }

    public async Task StopAsync()
    {
        Cancel();
        Task[] running;
        lock (_gate)
        {
            running = [.. _running];
        }

        await Task.WhenAll(running);
    }

    /// <summary>Single-line, bounded text safe for an environment variable.</summary>
    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(Math.Min(text.Length, MaximumFieldCharacters));
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
            if (builder.Length >= MaximumFieldCharacters)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private void Track(Task work)
    {
        lock (_gate)
        {
            if (!work.IsCompleted)
            {
                _running.Add(work);
            }
        }

        _ = work.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    _running.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DeliverAsync(
        OperatorNotifySettings settings,
        OperatorNotice notice,
        CancellationToken cancellationToken)
    {
        try
        {
            if (settings.DelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.DelaySeconds), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // The operator may answer while the command runs; only a session stop ends it early.
        var failure = await RunAsync(settings, notice, _stop.Token);
        if (failure is not null
            && !_stop.IsCancellationRequested
            && Interlocked.Exchange(ref _failureReported, 1) == 0)
        {
            _note("Operator notification failed: " + failure
                + " Later failures in this session are not reported.");
        }
    }

    private async Task<string?> RunAsync(
        OperatorNotifySettings settings,
        OperatorNotice notice,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = settings.Command[0],
            WorkingDirectory = _workspace(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        for (var index = 1; index < settings.Command.Count; index++)
        {
            process.StartInfo.ArgumentList.Add(settings.Command[index]);
        }

        var variables = Variables(notice);
        foreach (var (name, value) in variables)
        {
            process.StartInfo.Environment[name] = value;
        }

        try
        {
            if (!process.Start())
            {
                return "The command did not start.";
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "The command did not start: " + exception.Message;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var output = ProcessOutputReader.ReadStreamsAsync(process, timeout.Token);
        try
        {
            await WriteInputAsync(process, variables, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await output;
            return process.ExitCode == 0
                ? null
                : $"The command exited with code {process.ExitCode}.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"The command timed out after {settings.TimeoutSeconds} seconds.";
        }
        catch (OperationCanceledException)
        {
            return "The command was stopped.";
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return "The command output failed: " + exception.Message;
        }
        finally
        {
            TryKill(process);
            try
            {
                await output;
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
        }
    }

    private IReadOnlyList<KeyValuePair<string, string>> Variables(OperatorNotice notice)
    {
        var variables = new List<KeyValuePair<string, string>>
        {
            new("CRYSTAL_NOTIFY_EVENT", notice.Event.Value),
            new("CRYSTAL_NOTIFY_TITLE", Title),
            new("CRYSTAL_NOTIFY_TEXT", Clean(notice.Text)),
            new("CRYSTAL_WORKSPACE", _workspace()),
            new("CRYSTAL_SESSION", _session())
        };
        foreach (var (name, value) in notice.Fields)
        {
            variables.Add(new("CRYSTAL_NOTIFY_" + name, Clean(value)));
        }

        return variables;
    }

    /// <summary>
    /// The same variables as one JSON object on stdin. A command that exits
    /// without reading stdin is not a failure.
    /// </summary>
    private static async Task WriteInputAsync(
        Process process,
        IReadOnlyList<KeyValuePair<string, string>> variables,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(variables.ToDictionary(pair => pair.Key, pair => pair.Value));
            await process.StandardInput.WriteAsync(json.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private sealed class Pending(CancellationTokenSource? source) : IDisposable
    {
        public static Pending None { get; } = new(null);

        public void Dispose()
        {
            if (source is null)
            {
                return;
            }

            source.Cancel();
            source.Dispose();
        }
    }
}
