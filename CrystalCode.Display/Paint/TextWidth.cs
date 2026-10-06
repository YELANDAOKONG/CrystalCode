using System.Globalization;
using System.Text;

namespace CrystalCode.Display.Paint;

public static class TextWidth
{
    public const int TabColumns = 4;

    public static string ExpandTabs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\t", new string(' ', TabColumns), StringComparison.Ordinal);
    }

    public static int Measure(ReadOnlySpan<char> text)
    {
        var columns = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            columns += ColumnWidth(rune);
        }

        return columns;
    }

    public static int MoveLeft(ReadOnlySpan<char> text, int cursor)
    {
        if (cursor <= 0)
        {
            return 0;
        }

        var index = cursor - 1;
        if (index > 0
            && char.IsLowSurrogate(text[index])
            && char.IsHighSurrogate(text[index - 1]))
        {
            return index - 1;
        }

        return index;
    }

    public static int MoveRight(ReadOnlySpan<char> text, int cursor)
    {
        if (cursor >= text.Length)
        {
            return text.Length;
        }

        if (char.IsHighSurrogate(text[cursor])
            && cursor + 1 < text.Length
            && char.IsLowSurrogate(text[cursor + 1]))
        {
            return cursor + 2;
        }

        return cursor + 1;
    }

    public static List<string> Wrap(string text, int columnBudget)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (columnBudget < 1)
        {
            columnBudget = 1;
        }

        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var start = 0;
            while (start < paragraph.Length)
            {
                var end = FitEnd(paragraph, start, paragraph.Length, columnBudget);
                if (end <= start)
                {
                    end = MoveRight(paragraph, start);
                }

                lines.Add(paragraph[start..end]);
                start = end;
            }
        }

        return lines;
    }

    /// <summary>
    /// End index of the next wrapped row. Shared with markdown so both paths
    /// break on the same column without a second copy of the text.
    /// </summary>
    internal static int FitEnd(string text, int start, int limit, int columnBudget)
    {
        var columns = 0;
        var end = start;
        while (end < limit)
        {
            var next = MoveRight(text, end);
            var width = Measure(text.AsSpan(end, next - end));
            if (columns + width > columnBudget && end > start)
            {
                return end;
            }

            columns += width;
            end = next;
        }

        return end;
    }

    public static string Truncate(string text, int columnBudget)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (columnBudget < 1)
        {
            return string.Empty;
        }

        if (Measure(text) <= columnBudget)
        {
            return text;
        }

        const string Ellipsis = "...";
        var budget = Math.Max(columnBudget - Measure(Ellipsis), 0);
        if (budget == 0)
        {
            return Ellipsis[..Math.Min(Ellipsis.Length, columnBudget)];
        }

        var end = 0;
        var columns = 0;
        while (end < text.Length)
        {
            var next = MoveRight(text, end);
            var width = Measure(text.AsSpan(end, next - end));
            if (columns + width > budget)
            {
                break;
            }

            columns += width;
            end = next;
        }

        return text[..end] + Ellipsis;
    }

    public static (int Start, string Visible) Window(
        string text,
        int cursor,
        int columnBudget)
    {
        text = text.Replace('\n', ' ');
        cursor = Math.Clamp(cursor, 0, text.Length);
        var start = 0;
        while (start < cursor
            && Measure(text.AsSpan(start, cursor - start)) > columnBudget)
        {
            start = MoveRight(text, start);
        }

        var visible = new StringBuilder();
        var columns = 0;
        foreach (var rune in text.AsSpan(start).EnumerateRunes())
        {
            var width = ColumnWidth(rune);
            if (columns + width > columnBudget)
            {
                break;
            }

            visible.Append(rune);
            columns += width;
        }

        return (start, visible.ToString());
    }

    public static int ColumnWidth(Rune rune)
    {
        var value = rune.Value;
        if (value == '\t')
        {
            return TabColumns;
        }

        if (value == 0 || value < 0x20 || value == 0x7F)
        {
            return 0;
        }

        if (value < 0x7F)
        {
            return 1;
        }

        // Spectre counts a soft hyphen as one cell, so never measure it narrower.
        if (value == 0xAD)
        {
            return 1;
        }

        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format)
        {
            return 0;
        }

        if (category is UnicodeCategory.SpacingCombiningMark)
        {
            return 1;
        }

        return IsWide(value) ? 2 : 1;
    }

    private static bool IsWide(int value) => WideRanges.Contains(value);
}
