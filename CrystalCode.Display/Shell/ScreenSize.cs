namespace CrystalCode.Display.Shell;

/// <summary>
/// Current terminal size, with a fallback when the console is detached.
/// </summary>
public static class ScreenSize
{
    public static int Width => TryRead(out var width, out _) ? width : 80;

    public static int Height => TryRead(out _, out var height) ? height : 24;

    public static bool TryRead(out int width, out int height)
    {
        width = 80;
        height = 24;
        try
        {
            width = Math.Max(Console.WindowWidth, ShellLayout.MinWidth);
            height = Math.Max(Console.WindowHeight, ShellLayout.MinHeight);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static ShellRegions Current(
        int composerWanted,
        int overlayWanted,
        int queueWanted = 0) =>
        ShellLayout.Measure(Width, Height, composerWanted, overlayWanted, queueWanted);
}
