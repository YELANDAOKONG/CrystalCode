namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Follow-up prompts typed while a turn is running. Ctrl+C reaches it from
/// another thread, so every member takes the lock.
/// </summary>
public sealed class MessageQueue
{
    private readonly object _gate = new();
    private readonly List<(string Text, bool Interrupts)> _items = [];

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

    public bool InterruptAfterTools
    {
        get
        {
            lock (_gate)
            {
                return _items.Any(static item => item.Interrupts);
            }
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items.Select(static item => item.Text)];
        }
    }

    public void Enqueue(string text, bool interruptAfterTools = true, bool preserveText = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _items.Add((preserveText ? text : trimmed, interruptAfterTools));
        }
    }

    public string? Drain()
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return null;
            }

            var text = string.Join("\n\n", _items.Select(static item => item.Text));
            _items.Clear();
            return text;
        }
    }

    /// <summary>
    /// Removes the most recently queued message and returns it, or null when
    /// the queue is empty.
    /// </summary>
    public string? RemoveLast()
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return null;
            }

            var text = _items[^1].Text;
            _items.RemoveAt(_items.Count - 1);
            return text;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }
}
