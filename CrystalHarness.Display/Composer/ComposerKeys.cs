using CrystalHarness.Display.Input;

namespace CrystalHarness.Display.Composer;

/// <summary>
/// Platform-aware composer chords. Unix ReadKey tags <c>\\b</c> as Control.
/// macOS Option+Backspace is Alt. Windows Ctrl+Backspace deletes a word.
/// </summary>
internal static class ComposerKeys
{
    public static bool IsWordDeleteLeft(ConsoleKeyInfo key) =>
        IsWordDeleteLeft(InputKey.From(key), OperatingSystem.IsWindows());

    public static bool IsWordDeleteLeft(InputKey key) =>
        IsWordDeleteLeft(key, OperatingSystem.IsWindows());

    public static bool IsWordDeleteLeft(ConsoleKeyInfo key, bool windowsChords) =>
        IsWordDeleteLeft(InputKey.From(key), windowsChords);

    public static bool IsWordDeleteLeft(InputKey key, bool windowsChords)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Key != ConsoleKey.Backspace)
        {
            return false;
        }

        if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            return true;
        }

        if (!key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return false;
        }

        if (windowsChords)
        {
            return true;
        }

        return key.KeyChar is not ('\b' or '\u007f');
    }
}
