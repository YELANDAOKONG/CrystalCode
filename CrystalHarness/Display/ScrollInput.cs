using System.Text;

namespace CrystalHarness.Display;

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

        return TrySequence(burst, composerEmpty, pickerOpen, pageRows, out delta);
    }

    public static bool TryComposerKey(
        IReadOnlyList<ConsoleKeyInfo> burst,
        out ConsoleKeyInfo key)
    {
        ArgumentNullException.ThrowIfNull(burst);
        key = default;
        if (burst.Count == 1)
        {
            key = burst[0];
            return true;
        }

        var text = Chars(burst);
        if (text == "\u001b[A")
        {
            key = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
            return true;
        }

        if (text == "\u001b[B")
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

        return false;
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

        return false;
    }

    private static bool TryMouseWheel(string text, out int delta)
    {
        delta = 0;
        if (!text.StartsWith("\u001b[<", StringComparison.Ordinal)
            || (text[^1] is not 'M' and not 'm'))
        {
            return false;
        }

        var body = text[3..^1];
        var split = body.IndexOf(';');
        var button = split < 0 ? body : body[..split];
        if (button == "64")
        {
            delta = LineStep;
            return true;
        }

        if (button == "65")
        {
            delta = -LineStep;
            return true;
        }

        return false;
    }

    private static string Chars(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.KeyChar != '\0')
            {
                text.Append(key.KeyChar);
            }
        }

        return text.ToString();
    }
}
