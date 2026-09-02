using CrystalCode.Display.Input;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Product rules for display toggles that are not composer text.
/// </summary>
public static class DisplayInput
{
    public enum VerboseToggle
    {
        Tools,
        Commands
    }

    public static bool TryToggleVerbose(
        InputKey key,
        bool composerEmpty,
        bool pickerOpen,
        out VerboseToggle toggle)
    {
        ArgumentNullException.ThrowIfNull(key);
        toggle = VerboseToggle.Tools;
        if (!composerEmpty || pickerOpen)
        {
            return false;
        }

        if (!key.Modifiers.HasFlag(ConsoleModifiers.Control)
            || key.Modifiers.HasFlag(ConsoleModifiers.Alt)
            || key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.O:
                toggle = VerboseToggle.Tools;
                return true;
            case ConsoleKey.G:
                toggle = VerboseToggle.Commands;
                return true;
            default:
                return false;
        }
    }
}
