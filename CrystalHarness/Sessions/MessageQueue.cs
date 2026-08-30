namespace CrystalHarness.Sessions;

/// <summary>
/// Follow-up prompts typed while a turn is running.
/// </summary>
public sealed class MessageQueue
{
    private readonly List<string> _items = [];

    public int Count => _items.Count;

    public void Enqueue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        _items.Add(trimmed);
    }

    public string? Drain()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        var text = string.Join("\n\n", _items);
        _items.Clear();
        return text;
    }

    public void Clear()
    {
        _items.Clear();
    }
}
