namespace CrystalCode.Display.Shell;

/// <summary>
/// Current terminal size, with a fallback when the console is detached.
/// Width and Height clamp to the layout floor; TryRead reports the raw size.
/// </summary>
public static class ScreenSize
{
    public static int Width => TryRead(out var width, out _) ? Math.Max(width, ShellLayout.MinWidth) : 80;

    public static int Height => TryRead(out _, out var height) ? Math.Max(height, ShellLayout.MinHeight) : 24;

    public static bool TryRead(out int width, out int height)
    {
        width = 80;
        height = 24;
        try
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;
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
        int queueWanted = 0,
        int progressWanted = 0,
        int todoWanted = 0) =>
        ShellLayout.Measure(Width, Height, composerWanted, overlayWanted, queueWanted, progressWanted, todoWanted);
}
