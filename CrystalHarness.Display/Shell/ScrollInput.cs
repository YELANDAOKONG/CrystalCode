using System.Text;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Transcript scroll gestures from a key burst. CSI is not paste.
/// Up/Down arrows are reserved for input history and slash picker navigation.
/// Transcript scrolling uses PageUp/PageDown, Ctrl+Up/Down, and mouse wheel.
/// </summary>
public static class ScrollInput
{
    public const int LineStep = 3;

    public static bool TryDelta(
        IReadOnlyList<ConsoleKeyInfo> burst,
        bool composerEmpty,
        bool pickerOpen,
        int pageRows,
        out int delta)
    {
        ArgumentNullException.ThrowIfNull(burst);
        delta = 0;
        if (burst.Count == 0)
        {
            return false;
        }

        pageRows = Math.Max(1, pageRows);
        if (burst.Count == 1)
        {
            return TrySingleKey(burst[0], composerEmpty, pickerOpen, pageRows, out delta);
        }

        // 1007 wheel and Linux ReadKey both batch arrows. One Up is history.
        if (TryRepeatedArrowKeys(burst, out delta))
        {
            return true;
        }

        return TrySequence(burst, composerEmpty, pickerOpen, pageRows, out delta);
    }

    public static bool TryComposerKeys(
        IReadOnlyList<ConsoleKeyInfo> burst,
        out IReadOnlyList<ConsoleKeyInfo> keys)
    {
        ArgumentNullException.ThrowIfNull(burst);
        keys = [];
        if (TryComposerKey(burst, out var key))
        {
            keys = [key];
            return true;
        }

        if (AreEditRepeats(burst))
        {
            keys = burst;
            return true;
        }

        return false;
    }

    public static bool TryComposerKey(
        IReadOnlyList<ConsoleKeyInfo> burst,
        out ConsoleKeyInfo key)
    {
        ArgumentNullException.ThrowIfNull(burst);
        key = default;
        if (burst.Count == 1)
        {
            key = Canonical(burst[0]);
            return true;
        }

        if (TryReturnBurst(burst, out key))
        {
            return true;
        }

        var text = Chars(burst);
        if (text is "\u001b[A" or "\u001bOA")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
            return true;
        }

        if (text is "\u001b[B" or "\u001bOB")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false);
            return true;
        }

        if (text == "\u001b[C")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false);
            return true;
        }

        if (text == "\u001b[D")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);
            return true;
        }

        if (text == "\u001b[H" || text == "\u001b[1~")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false);
            return true;
        }

        if (text == "\u001b[F" || text == "\u001b[4~")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false);
            return true;
        }

        if (text == "\u001b[3~")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false);
            return true;
        }

        if (text is "\u001b[127u" or "\u001b[27;1;127~" or "\u001b[27;1;8~")
        {
            key = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
            return true;
        }

        if (text is "\u001b[Z" or "\u001b[9;2u")
        {
            key = new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false);
            return true;
        }

        if (text is "\u001b[9u" or "\u001b[27;1;9~")
        {
            key = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
            return true;
        }

        if (text is "\u001b[13u" or "\u001b[27;1;13~")
        {
            key = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            return true;
        }

        return false;
    }

    // Windows VT input leaves Key empty; Tab/Enter live in KeyChar. CR+LF is one Enter.
    private static ConsoleKeyInfo Canonical(ConsoleKeyInfo key)
    {
        if (key.Key != default)
        {
            return key;
        }

        var mapped = key.KeyChar switch
        {
            '\t' => ConsoleKey.Tab,
            '\r' => ConsoleKey.Enter,
            '\u001b' => ConsoleKey.Escape,
            '\b' or '\u007f' => ConsoleKey.Backspace,
            >= 'A' and <= 'Z' => (ConsoleKey)key.KeyChar,
            >= 'a' and <= 'z' => (ConsoleKey)char.ToUpperInvariant(key.KeyChar),
            >= '0' and <= '9' => (ConsoleKey)key.KeyChar,
            _ => default
        };
        return mapped == default ? key : WithKey(key, mapped);
    }

    private static bool TryReturnBurst(
        IReadOnlyList<ConsoleKeyInfo> burst,
        out ConsoleKeyInfo key)
    {
        key = default;
        if (burst.Count != 2)
        {
            return false;
        }

        if (!IsReturn(burst[0]) || !IsReturn(burst[1]))
        {
            return false;
        }

        key = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        return true;
    }

    private static bool IsReturn(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n';

    private static ConsoleKeyInfo WithKey(ConsoleKeyInfo key, ConsoleKey mapped) =>
        new(
            key.KeyChar,
            mapped,
            key.Modifiers.HasFlag(ConsoleModifiers.Shift),
            key.Modifiers.HasFlag(ConsoleModifiers.Alt),
            key.Modifiers.HasFlag(ConsoleModifiers.Control));

    private static bool AreEditRepeats(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        if (burst.Count <= 1)
        {
            return false;
        }

        foreach (var key in burst)
        {
            if (key.Key is not (ConsoleKey.Backspace or ConsoleKey.Delete)
                && key.KeyChar is not ('\b' or '\u007f'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsPaste(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        ArgumentNullException.ThrowIfNull(burst);
        if (burst.Count <= 1)
        {
            return false;
        }

        if (burst[0].Key == ConsoleKey.Escape || burst[0].KeyChar == '\u001b')
        {
            return false;
        }

        foreach (var key in burst)
        {
            if (!char.IsControl(key.KeyChar) || key.Key == ConsoleKey.Enter)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySingleKey(
        ConsoleKeyInfo key,
        bool composerEmpty,
        bool pickerOpen,
        int pageRows,
        out int delta)
    {
        delta = 0;
        if (key.Key == ConsoleKey.PageUp)
        {
            delta = pageRows;
            return true;
        }

        if (key.Key == ConsoleKey.PageDown)
        {
            delta = -pageRows;
            return true;
        }

        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        if (key.Key == ConsoleKey.UpArrow && control)
        {
            delta = LineStep;
            return true;
        }

        if (key.Key == ConsoleKey.DownArrow && control)
        {
            delta = -LineStep;
            return true;
        }

        if (key.Key == ConsoleKey.UpArrow && composerEmpty && !pickerOpen)
        {
            delta = LineStep;
            return true;
        }

        if (key.Key == ConsoleKey.DownArrow && composerEmpty && !pickerOpen)
        {
            delta = -LineStep;
            return true;
        }

        return false;
    }

    private static bool TrySequence(
        IReadOnlyList<ConsoleKeyInfo> burst,
        bool composerEmpty,
        bool pickerOpen,
        int pageRows,
        out int delta)
    {
        delta = 0;
        var text = Chars(burst);
        if (text.Length == 0)
        {
            return false;
        }

        if (TryMouseWheel(text, out delta))
        {
            return true;
        }

        if (text.Contains("[5~", StringComparison.Ordinal))
        {
            delta = pageRows;
            return true;
        }

        if (text.Contains("[6~", StringComparison.Ordinal))
        {
            delta = -pageRows;
            return true;
        }

        if (text.Contains("[1;5A", StringComparison.Ordinal) || text.EndsWith("[5A", StringComparison.Ordinal))
        {
            delta = LineStep;
            return true;
        }

        if (text.Contains("[1;5B", StringComparison.Ordinal) || text.EndsWith("[5B", StringComparison.Ordinal))
        {
            delta = -LineStep;
            return true;
        }

        if (TryCsiArrowScroll(text, composerEmpty, pickerOpen, out delta))
        {
            return true;
        }

        return false;
    }

    private static bool TryRepeatedArrowKeys(
        IReadOnlyList<ConsoleKeyInfo> burst,
        out int delta)
    {
        delta = 0;
        if (burst.Count < 2)
        {
            return false;
        }

        var ups = 0;
        var downs = 0;
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.UpArrow)
            {
                ups++;
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                downs++;
                continue;
            }

            return false;
        }

        delta = (ups - downs) * LineStep;
        return true;
    }

    private static bool TryCsiArrowScroll(
        string text,
        bool composerEmpty,
        bool pickerOpen,
        out int delta)
    {
        delta = 0;
        if (!TryCountCsiArrows(text, out var ups, out var downs))
        {
            return false;
        }

        var ticks = ups + downs;
        if (ticks == 1 && (!composerEmpty || pickerOpen))
        {
            return false;
        }

        delta = (ups - downs) * LineStep;
        return true;
    }

    private static bool TryCountCsiArrows(string text, out int ups, out int downs)
    {
        ups = 0;
        downs = 0;
        var index = 0;
        while (index < text.Length)
        {
            if (TryConsume(text, ref index, "\u001b[A") || TryConsume(text, ref index, "\u001bOA"))
            {
                ups++;
                continue;
            }

            if (TryConsume(text, ref index, "\u001b[B") || TryConsume(text, ref index, "\u001bOB"))
            {
                downs++;
                continue;
            }

            return false;
        }

        return ups + downs > 0;
    }

    private static bool TryConsume(string text, ref int index, string sequence)
    {
        if (index + sequence.Length > text.Length)
        {
            return false;
        }

        if (string.CompareOrdinal(text, index, sequence, 0, sequence.Length) != 0)
        {
            return false;
        }

        index += sequence.Length;
        return true;
    }

    private static bool TryMouseWheel(string text, out int delta)
    {
        delta = 0;
        var found = false;
        var index = 0;
        while (index < text.Length)
        {
            var sgr = text.IndexOf("[<", index, StringComparison.Ordinal);
            var x10 = text.IndexOf("[M", index, StringComparison.Ordinal);
            if (sgr < 0 && x10 < 0)
            {
                break;
            }

            if (sgr >= 0 && (x10 < 0 || sgr <= x10))
            {
                if (TryReadSgrWheel(text, sgr + 2, out var step, out var next))
                {
                    delta += step;
                    found = true;
                    index = next;
                    continue;
                }

                index = sgr + 2;
                continue;
            }

            if (TryReadX10Wheel(text, x10 + 2, out var x10Step))
            {
                delta += x10Step;
                found = true;
                index = x10 + 5;
                continue;
            }

            index = x10 + 2;
        }

        return found;
    }

    private static bool TryReadSgrWheel(string text, int start, out int step, out int next)
    {
        step = 0;
        next = start;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        if (end == start || !int.TryParse(text.AsSpan(start, end - start), out var button))
        {
            return false;
        }

        var close = text.IndexOfAny(['M', 'm'], end);
        if (close < 0)
        {
            return false;
        }

        next = close + 1;
        if ((button & 64) == 0)
        {
            return false;
        }

        step = (button & 1) == 0 ? LineStep : -LineStep;
        return true;
    }

    private static bool TryReadX10Wheel(string text, int start, out int step)
    {
        step = 0;
        if (start + 2 >= text.Length)
        {
            return false;
        }

        var button = text[start] - 32;
        if ((button & 64) == 0)
        {
            return false;
        }

        step = (button & 1) == 0 ? LineStep : -LineStep;
        return true;
    }

    private static string Chars(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                text.Append('\u001b');
                if (key.KeyChar is not ('\0' or '\u001b'))
                {
                    text.Append(key.KeyChar);
                }

                continue;
            }

            if (key.KeyChar != '\0')
            {
                text.Append(key.KeyChar);
            }
        }

        return text.ToString();
    }
}
