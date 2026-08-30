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
        return Clip(FirstContentLine(text));
    }

    public static string Summary(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Clip(FirstContentLine(text));
    }

    private static string FirstContentLine(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        string? exit = null;
        foreach (var raw in normalized.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            if (exit is null && line.StartsWith("exit ", StringComparison.Ordinal))
            {
                exit = line;
                continue;
            }

            return line;
        }

        return exit ?? string.Empty;
    }

    private static string Clip(string line) =>
        line.Length <= MaximumLength ? line : line[..(MaximumLength - 3)] + "...";
}
