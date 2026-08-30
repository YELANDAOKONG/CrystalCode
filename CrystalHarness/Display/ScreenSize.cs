namespace CrystalHarness.Display;

/// <summary>
/// Current terminal size, with a fallback when the console is detached.
/// </summary>
public static class ScreenSize
{
    public static int Width
    {
        get
        {
            try
            {
                return Math.Max(Console.WindowWidth, ShellLayout.MinWidth);
            }
            catch (IOException)
            {
                return 80;
            }
        }
    }

    public static int Height
    {
        get
        {
            try
            {
                return Math.Max(Console.WindowHeight, ShellLayout.MinHeight);
            }
            catch (IOException)
            {
                return 24;
            }
        }
    }

    public static ShellRegions Current(
        int composerWanted,
        int overlayWanted,
        int queueWanted = 0) =>
        ShellLayout.Measure(Width, Height, composerWanted, overlayWanted, queueWanted);
}
