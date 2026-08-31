namespace CrystalCode.Display.Paint;

/// <summary>
/// Accumulates streamed tool-name deltas per tool call so sequential or
/// interleaved calls in one round never bleed into a single label.
/// </summary>
public sealed class StreamToolNames
{
    private readonly Dictionary<(int Candidate, int Item), string> _names = [];

    /// <summary>
    /// Applies one name delta to the named tool call and returns its
    /// accumulated name. The first delta for a call is its snapshot.
    /// </summary>
    public string Apply(int candidateIndex, int itemIndex, string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        var key = (candidateIndex, itemIndex);
        _names.TryGetValue(key, out var current);
        var next = StreamName.Apply(current ?? string.Empty, delta);
        _names[key] = next;
        return next;
    }

    public void Clear() => _names.Clear();
}
