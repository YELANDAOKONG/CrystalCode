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

        if (key.Key != ConsoleKey.O
            || !key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return false;
        }

        toggle = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
            ? VerboseToggle.Commands
            : VerboseToggle.Tools;
        return true;
    }
}
