using Crystal.Tools;

namespace CrystalCode.Run;

/// <summary>
/// Shortens tool output for the plain run log. JSON format keeps the full text.
/// </summary>
internal static class RunToolBody
{
    private const int ExcerptLines = 12;

    private const int CommandTailLines = 8;

    private const int CommandFailureTailLines = 32;

    public static string Format(string? toolName, ToolResultStatus status, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        if (IsEditOrWrite(toolName))
        {
            return normalized;
        }

        if (IsCommand(toolName))
        {
            var tailLines = status == ToolResultStatus.Failure
                ? CommandFailureTailLines
                : CommandTailLines;
            return Command(normalized, tailLines);
        }

        if (status == ToolResultStatus.Failure)
        {
            return normalized;
        }

        return Head(normalized, ExcerptLines);
    }

    private static bool IsCommand(string? toolName) =>
        string.Equals(toolName, "bash", StringComparison.OrdinalIgnoreCase);

    private static bool IsEditOrWrite(string? toolName) =>
        string.Equals(toolName, "edit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "write", StringComparison.OrdinalIgnoreCase);

    private static string Head(string text, int maxLines)
    {
        var lines = Split(text);
        if (lines.Length <= maxLines)
        {
            return text;
        }

        var kept = new string[maxLines + 1];
        for (var index = 0; index < maxLines; index++)
        {
            kept[index] = lines[index];
        }

        kept[maxLines] = Omitted(lines.Length - maxLines, earlier: false);
        return string.Join('\n', kept);
    }

    private static string Command(string text, int tailLines)
    {
        var lines = Split(text);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var start = 0;
        string? exit = null;
        if (lines[0].StartsWith("exit ", StringComparison.Ordinal))
        {
            exit = lines[0];
            start = 1;
        }

        var bodyCount = lines.Length - start;
        var kept = Math.Min(tailLines, bodyCount);
        var omitted = bodyCount - kept;
        var parts = new List<string>(kept + 2);
        if (exit is not null)
        {
            parts.Add(exit);
        }

        if (omitted > 0)
        {
            parts.Add(Omitted(omitted, earlier: true));
        }

        for (var index = lines.Length - kept; index < lines.Length; index++)
        {
            parts.Add(lines[index]);
        }

        return string.Join('\n', parts);
    }

    private static string Omitted(int count, bool earlier)
    {
        if (count == 1)
        {
            return earlier ? "... 1 earlier line omitted" : "... 1 line omitted";
        }

        return earlier
            ? $"... {count} earlier lines omitted"
            : $"... {count} lines omitted";
    }

    private static string[] Split(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        return text.Split('\n');
    }
}
