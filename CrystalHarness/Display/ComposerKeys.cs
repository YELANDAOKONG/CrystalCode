namespace CrystalHarness.Display;

/// <summary>
/// Platform-aware composer chords. Unix ReadKey tags <c>\\b</c> as Control.
/// </summary>
internal static class ComposerKeys
{
    public static bool IsWordDeleteLeft(ConsoleKeyInfo key) =>
        IsWordDeleteLeft(key, OperatingSystem.IsWindows());

    public static bool IsWordDeleteLeft(ConsoleKeyInfo key, bool windowsChords)
    {
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
