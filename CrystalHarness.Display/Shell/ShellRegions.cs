namespace CrystalHarness.Display.Shell;

/// <summary>
/// Row split for one painted frame. Tops are zero-based.
/// </summary>
public readonly record struct ShellRegions(
    int Width,
    int Height,
    int TranscriptRows,
    int OverlayRows,
    int OverlayTop,
    int StatusTop,
    int QueueRows,
    int QueueTop,
    int ComposerRows,
    int ComposerTop);
