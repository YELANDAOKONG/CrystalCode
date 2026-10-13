namespace CrystalCode.Engine.Tools;

/// <summary>
/// Owns background Bash tasks for one live session. Tasks are not persisted.
/// </summary>
public sealed class BackgroundTaskRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, BackgroundBashTask> _tasks = new(StringComparer.Ordinal);
    private readonly Workspace _workspace;
    private readonly int? _timeoutSeconds;
    private bool _disposed;

    public BackgroundTaskRegistry(
        Workspace workspace,
        int? timeoutSeconds = WorkspaceLimits.BashTimeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!BashTool.IsSupported(timeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds),
                timeoutSeconds,
                "Bash timeout must be a positive supported number of seconds, or unlimited.");
        }

        _workspace = workspace;
        _timeoutSeconds = timeoutSeconds;
    }

    public bool TryStart(
        string command,
        out BackgroundTaskSnapshot snapshot,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(command);
        snapshot = null!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            error = "Arguments must include a command string.";
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                error = "The session is closed.";
                return false;
            }

            var activeCount = _tasks.Values.Count(static task => task.IsActive);
            if (activeCount >= WorkspaceLimits.MaximumBackgroundTasks)
            {
                error = $"The session already has {WorkspaceLimits.MaximumBackgroundTasks} active background tasks.";
                return false;
            }

            var taskId = "task_" + Guid.NewGuid().ToString("N")[..12];
            var task = new BackgroundBashTask(taskId, command, _workspace.Root, _timeoutSeconds);
            if (!task.Start())
            {
                error = task.StartError;
                return false;
            }

            _tasks.Add(taskId, task);
            snapshot = task.Snapshot();
            return true;
        }
    }

    public bool TryGetSnapshot(string taskId, out BackgroundTaskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        lock (_gate)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                snapshot = task.Snapshot();
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    public bool TryReadLogs(
        string taskId,
        int offset,
        int limit,
        out BackgroundTaskLog log)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        if (offset < 0)
        {
            log = new BackgroundTaskLog("The log offset must not be negative.", offset, false, false);
            return false;
        }

        if (limit <= 0 || limit > WorkspaceLimits.MaximumBackgroundLogCharacters)
        {
            log = new BackgroundTaskLog(
                $"The log limit must be between 1 and {WorkspaceLimits.MaximumBackgroundLogCharacters}.",
                offset,
                false,
                false);
            return false;
        }

        lock (_gate)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                log = task.ReadLogs(offset, limit);
                return !log.OffsetExpired;
            }
        }

        log = new BackgroundTaskLog("The background task was not found in this session.", offset, false, false);
        return false;
    }

    public async Task<BackgroundTaskSnapshot?> StopAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        BackgroundBashTask? task;
        lock (_gate)
        {
            _tasks.TryGetValue(taskId, out task);
        }

        if (task is null)
        {
            return null;
        }

        await task.StopAsync(cancellationToken);
        return task.Snapshot();
    }

    public async Task StopAllAsync()
    {
        BackgroundBashTask[] tasks;
        lock (_gate)
        {
            tasks = [.. _tasks.Values];
        }

        foreach (var task in tasks)
        {
            await task.StopAsync();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var task in _tasks.Values)
            {
                task.Stop();
            }
        }
    }
}
