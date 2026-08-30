namespace CrystalHarness.Tools;

/// <summary>
/// In-memory session todo list used by todowrite and compaction pins.
/// </summary>
public sealed class TodoList
{
    private const int MaximumCount = 50;
    private readonly object _gate = new();
    private readonly List<TodoItem> _items = [];

    public int Version { get; private set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public IReadOnlyList<TodoItem> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            Version++;
        }
    }

    public string Replace(IReadOnlyList<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            if (items.Count > MaximumCount)
            {
                return $"At most {MaximumCount} todos are allowed.";
            }

            _items.Clear();
            _items.AddRange(items);
            Version++;
            return FormatUnlocked();
        }
    }

    public string Merge(IReadOnlyList<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            if (items.Count == 0)
            {
                return FormatUnlocked();
            }

            var incomingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                incomingIds.Add(item.Id);
            }

            var kept = _items.Where(item => !incomingIds.Contains(item.Id)).ToList();
            if (items.Count + kept.Count > MaximumCount)
            {
                return $"At most {MaximumCount} todos are allowed.";
            }

            _items.Clear();
            _items.AddRange(items);
            _items.AddRange(kept);
            Version++;
            return FormatUnlocked();
        }
    }

    public string Format()
    {
        lock (_gate)
        {
            return FormatUnlocked();
        }
    }

    private string FormatUnlocked()
    {
        if (_items.Count == 0)
        {
            return "No todos.";
        }

        var inProgress = _items.Count(item => item.Status == TodoStatus.InProgress);
        var pending = _items.Count(item => item.Status == TodoStatus.Pending);
        var completed = _items.Count(item => item.Status == TodoStatus.Completed);
        var cancelled = _items.Count(item => item.Status == TodoStatus.Cancelled);
        var lines = new List<string>
        {
            $"{_items.Count} todos ({inProgress} in progress, {pending} pending, "
            + $"{completed} completed, {cancelled} cancelled)"
        };
        foreach (var item in _items)
        {
            lines.Add($"- [{StatusName(item.Status)}] {item.Content}");
        }

        return string.Join('\n', lines);
    }

    internal static string StatusName(TodoStatus status) =>
        status switch
        {
            TodoStatus.Pending => "pending",
            TodoStatus.InProgress => "in_progress",
            TodoStatus.Completed => "completed",
            TodoStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    internal static bool TryParseStatus(string? value, out TodoStatus status)
    {
        status = TodoStatus.Pending;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "pending":
                status = TodoStatus.Pending;
                return true;
            case "in_progress":
                status = TodoStatus.InProgress;
                return true;
            case "completed":
                status = TodoStatus.Completed;
                return true;
            case "cancelled":
                status = TodoStatus.Cancelled;
                return true;
            default:
                return false;
        }
    }
}
