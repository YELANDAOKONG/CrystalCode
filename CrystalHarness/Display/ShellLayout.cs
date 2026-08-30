namespace CrystalHarness.Display;

/// <summary>
/// Splits the terminal into transcript, overlay, status, and composer.
/// </summary>
public static class ShellLayout
{
    public const int StatusRows = 1;

    public const int MaxComposerRows = 8;

    public const int MinTranscriptRows = 3;

    public const int MinWidth = 16;

    public const int MinHeight = 8;

    public static ShellRegions Measure(
        int width,
        int height,
        int composerWanted,
        int overlayWanted)
    {
        width = Math.Max(width, MinWidth);
        height = Math.Max(height, MinHeight);
        var overlay = Math.Clamp(overlayWanted, 0, height - MinTranscriptRows - StatusRows - 1);
        var composer = Math.Clamp(composerWanted, 1, MaxComposerRows);
        var reserved = StatusRows + overlay + composer;
        var transcript = height - reserved;
        if (transcript < MinTranscriptRows)
        {
            composer = Math.Max(1, height - MinTranscriptRows - StatusRows - overlay);
            transcript = height - StatusRows - overlay - composer;
        }

        var overlayTop = transcript;
        var statusTop = overlayTop + overlay;
        var composerTop = statusTop + StatusRows;
        return new ShellRegions(
            width,
            height,
            transcript,
            overlay,
            overlayTop,
            statusTop,
            composer,
            composerTop);
    }
}
