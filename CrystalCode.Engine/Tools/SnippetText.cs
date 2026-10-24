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

    /// <summary>
    /// Lists the changed lines with up to <see cref="ContextLines"/> lines of
    /// context on each side, using the same <c>number|text</c> layout as the
    /// read tool. Returns an empty string when the file has no lines, as after
    /// a change that empties it.
    /// </summary>
    /// <param name="text">The full file text after the change.</param>
    /// <param name="firstLine">The 1-based first changed line.</param>
    /// <param name="changedLines">How many lines the change now occupies.</param>
    public static string Around(string text, int firstLine, int changedLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = SplitLines(text);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var last = Math.Max(firstLine, firstLine + changedLines - 1);
        var start = Math.Max(1, firstLine - ContextLines);
        var end = Math.Min(lines.Count, last + ContextLines);
        if (start > end)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var number = start; number <= end; number++)
        {
            builder.Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(6));
            builder.Append('|');
            builder.Append(lines[number - 1]);
            if (number < end)
            {
                builder.Append(ToolOutputText.LineSeparator);
            }
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
        var lines = new List<string>(text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n'));
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
