using System.Globalization;
using System.Text;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Numbered excerpt of a file around a change, appended to edit, write, and
/// patch results so the model can check the outcome without another read.
/// </summary>
internal static class SnippetText
{
    public const int ContextLines = 5;

    public const int MaximumGroups = 3;

    private const char ByteOrderMark = '\uFEFF';

    /// <summary>
    /// Lists the changed lines with up to <see cref="ContextLines"/> lines of
    /// context on each side, using the same <c>number|text</c> layout as the
    /// read tool. Returns an empty string when the file has no lines, as after
    /// a change that empties it.
    /// </summary>
    /// <param name="text">The full file text after the change.</param>
    /// <param name="firstLine">The 1-based first changed line.</param>
    /// <param name="changedLines">How many lines the change now occupies.</param>
    public static string Around(string text, int firstLine, int changedLines) =>
        Around(text, [new SnippetRegion(firstLine, changedLines)]);

    /// <summary>
    /// Like the single-region overload, for several changes in one file.
    /// Regions whose context windows touch are shown as one group, and groups
    /// are separated by <c>...</c>. Only the first
    /// <see cref="MaximumGroups"/> groups are shown.
    /// </summary>
    public static string Around(string text, IReadOnlyList<SnippetRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(regions);
        var lines = SplitLines(text);
        if (lines.Count == 0 || regions.Count == 0)
        {
            return string.Empty;
        }

        var ranges = new List<(int Start, int End)>();
        foreach (var region in regions.OrderBy(static region => region.FirstLine))
        {
            var first = Math.Clamp(region.FirstLine, 1, lines.Count);
            var last = Math.Clamp(region.FirstLine + region.LineCount - 1, first, lines.Count);
            var start = Math.Max(1, first - ContextLines);
            var end = Math.Min(lines.Count, last + ContextLines);
            if (ranges.Count > 0 && start <= ranges[^1].End + 1)
            {
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        var builder = new StringBuilder();
        var shown = Math.Min(ranges.Count, MaximumGroups);
        for (var group = 0; group < shown; group++)
        {
            if (group > 0)
            {
                builder.Append(ToolOutputText.LineSeparator);
                builder.Append("...");
                builder.Append(ToolOutputText.LineSeparator);
            }

            var (rangeStart, rangeEnd) = ranges[group];
            for (var number = rangeStart; number <= rangeEnd; number++)
            {
                builder.Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(6));
                builder.Append('|');
                builder.Append(lines[number - 1]);
                if (number < rangeEnd)
                {
                    builder.Append(ToolOutputText.LineSeparator);
                }
            }
        }

        if (ranges.Count > shown)
        {
            builder.Append(ToolOutputText.LineSeparator);
            builder.Append(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{ranges.Count - shown} more changed regions not shown]"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 1-based line number of the character at <paramref name="offset"/>.
    /// </summary>
    public static int LineOf(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = 1;
        var limit = Math.Min(offset, text.Length);
        for (var index = 0; index < limit; index++)
        {
            if (text[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Number of lines a piece of replacement text occupies. Empty text
    /// occupies none, and a trailing newline does not add a line.
    /// </summary>
    public static int CountLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? 0 : SplitLines(text).Count;
    }

    private static List<string> SplitLines(string text)
    {
        var body = text.Length > 0 && text[0] == ByteOrderMark ? text[1..] : text;
        var lines = new List<string>(body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n'));
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
