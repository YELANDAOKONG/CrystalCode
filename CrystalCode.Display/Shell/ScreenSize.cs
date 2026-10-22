namespace CrystalCode.Display.Shell;

/// <summary>
/// Current terminal size, with a fallback when the console is detached.
/// A non-positive read counts as detached: Unix reports 0 instead of throwing
/// when there is no terminal. Width and Height clamp a real size to the layout
/// floor; TryRead reports the raw size, or the fallback when the read fails.
/// </summary>
public static class ScreenSize
{
    internal const int FallbackWidth = 80;

    internal const int FallbackHeight = 24;

    public static int Width => TryRead(out var width, out _)
        ? Math.Max(width, ShellLayout.MinWidth)
        : FallbackWidth;

    public static int Height => TryRead(out _, out var height)
        ? Math.Max(height, ShellLayout.MinHeight)
        : FallbackHeight;

    public static bool TryRead(out int width, out int height)
    {
        width = FallbackWidth;
        height = FallbackHeight;
        try
        {
            return TryAccept(Console.WindowWidth, Console.WindowHeight, out width, out height);
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

    internal static bool TryAccept(int width, int height, out int acceptedWidth, out int acceptedHeight)
    {
        if (width < 1 || height < 1)
        {
            acceptedWidth = FallbackWidth;
            acceptedHeight = FallbackHeight;
            return false;
        }

        acceptedWidth = width;
        acceptedHeight = height;
        return true;
    }

    public static ShellRegions Current(
        int composerWanted,
        int overlayWanted,
        int queueWanted = 0,
        int progressWanted = 0,
        int todoWanted = 0) =>
        ShellLayout.Measure(Width, Height, composerWanted, overlayWanted, queueWanted, progressWanted, todoWanted);
}
