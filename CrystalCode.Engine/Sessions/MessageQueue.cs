namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Follow-up prompts typed while a turn is running.
/// </summary>
public sealed class MessageQueue
{
    private readonly List<string> _items = [];

    public int Count => _items.Count;

    public bool InterruptAfterTools { get; private set; }

    public IReadOnlyList<string> Snapshot() => [.. _items];

    public void Enqueue(string text, bool interruptAfterTools = true, bool preserveText = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        _items.Add(preserveText ? text : trimmed);
        InterruptAfterTools |= interruptAfterTools;
    }

    public string? Drain()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        var text = string.Join("\n\n", _items);
        Clear();
        return text;
    }

    public void Clear()
    {
        _items.Clear();
        InterruptAfterTools = false;
    }
}
