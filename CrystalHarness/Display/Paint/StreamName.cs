namespace CrystalHarness.Display.Paint;

/// <summary>
/// Coalesces tool-name stream chunks. Providers may send a snapshot or a delta.
/// </summary>
public static class StreamName
{
    public static string Apply(string current, string incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.Length == 0)
        {
            return current;
        }

        if (current.Length == 0)
        {
            return incoming;
        }

        if (incoming.StartsWith(current, StringComparison.Ordinal))
        {
            return incoming;
        }

        if (current.StartsWith(incoming, StringComparison.Ordinal)
            || current.EndsWith(incoming, StringComparison.Ordinal))
        {
            return current;
        }

        return current + incoming;
    }
}
