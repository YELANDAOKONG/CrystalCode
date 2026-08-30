using System.Globalization;
using System.Text;

namespace CrystalHarness.Display;

internal static class TextWidth
{
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
        if (value == 0 || value < 0x20 || value == 0x7F)
        {
            return 0;
        }

        if (value < 0x7F)
        {
            return 1;
        }

        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark)
        {
            return 0;
        }

        return IsWide(value) ? 2 : 1;
    }

    private static bool IsWide(int value) =>
        value is (>= 0x1100 and <= 0x115F)
            or (>= 0x2329 and <= 0x232A)
            or (>= 0x2E80 and <= 0xA4CF)
            or (>= 0xA960 and <= 0xA97C)
            or (>= 0xAC00 and <= 0xD7A3)
            or (>= 0xF900 and <= 0xFAFF)
            or (>= 0xFE10 and <= 0xFE19)
            or (>= 0xFE30 and <= 0xFE6F)
            or (>= 0xFF00 and <= 0xFF60)
            or (>= 0xFFE0 and <= 0xFFE6)
            or (>= 0x1F300 and <= 0x1F64F)
            or (>= 0x1F900 and <= 0x1F9FF)
            or (>= 0x20000 and <= 0x3FFFD);
}
