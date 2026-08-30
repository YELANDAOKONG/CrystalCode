namespace CrystalHarness.Display;

/// <summary>
/// One-line tool output for the transcript.
/// </summary>
public static class ToolResultText
{
    public const int MaximumLength = 100;

    public static string FirstLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = normalized.IndexOf('\n');
        var line = end < 0 ? normalized : normalized[..end];
        return line.Length <= MaximumLength
            ? line
            : line[..(MaximumLength - 3)] + "...";
    }
}
