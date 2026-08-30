namespace CrystalHarness.Display;

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
    int ComposerRows,
    int ComposerTop);
