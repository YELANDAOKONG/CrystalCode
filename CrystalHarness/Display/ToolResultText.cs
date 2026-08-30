namespace CrystalHarness.Display;

/// <summary>
/// One-line tool output for the transcript.
/// </summary>
public static class ToolResultText
{
    public const int MaximumLength = 100;

    public const int MaximumBodyLines = 16;

    public const int MaximumBodyLength = 1200;

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

    public static string Body(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var totalNonEmptyLines = 0;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            if (lines.Count == 0 && line.StartsWith("exit ", StringComparison.Ordinal))
            {
                continue;
            }

            totalNonEmptyLines++;
            if (lines.Count < MaximumBodyLines)
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return FirstContentLine(text);
        }

        if (totalNonEmptyLines > MaximumBodyLines)
        {
            var omitted = totalNonEmptyLines - MaximumBodyLines;
            lines.Add($"... ({omitted} more lines)");
        }

        var joined = string.Join('\n', lines);
        return joined.Length <= MaximumBodyLength
            ? joined
            : joined[..(MaximumBodyLength - 3)] + "...";
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
