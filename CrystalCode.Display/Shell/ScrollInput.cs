using CrystalCode.Display.Input;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Product rules for which decoded keys scroll the transcript.
/// CSI and wheel bursts are already events; this does not parse VT.
/// </summary>
public static class ScrollInput
{
    public const int LineStep = InputWheel.LineStep;

    public static bool TryKeyScroll(
        InputKey key,
        bool composerEmpty,
        bool pickerOpen,
        int pageRows,
        out int delta)
    {
        ArgumentNullException.ThrowIfNull(key);
        delta = 0;
        pageRows = Math.Max(1, pageRows);
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
}
