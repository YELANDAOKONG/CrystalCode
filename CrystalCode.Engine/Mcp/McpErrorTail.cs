namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Keeps the last few lines a stdio server wrote to stderr so a failed start
/// can say why. The lines are for the operator only and never reach the model.
/// </summary>
internal sealed class McpErrorTail
{
    private const int MaximumLines = 8;
    private const int MaximumLineLength = 300;

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();

    public void Add(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var clean = Clean(line);
        if (clean.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _lines.Enqueue(clean);
            while (_lines.Count > MaximumLines)
            {
                _lines.Dequeue();
            }
        }
    }

    public string Text()
    {
        lock (_gate)
        {
            return string.Join(" | ", _lines);
        }
    }

    // Control characters would let a server drive the terminal through a note.
    private static string Clean(string line)
    {
        var chars = line.Select(character => char.IsControl(character) ? ' ' : character).ToArray();
        var text = new string(chars).Trim();
        return text.Length <= MaximumLineLength ? text : text[..MaximumLineLength];
    }
}
