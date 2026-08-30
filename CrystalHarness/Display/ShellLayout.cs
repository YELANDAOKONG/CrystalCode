namespace CrystalHarness.Display;

/// <summary>
/// Splits the terminal into transcript, overlay, status, queue, and composer.
/// </summary>
public static class ShellLayout
{
    public const int StatusRows = 1;

    public const int MaxComposerRows = 8;

    public const int MaxQueueRows = 8;

    public const int MinTranscriptRows = 3;

    public const int MinWidth = 16;

    public const int MinHeight = 8;

    public static ShellRegions Measure(
        int width,
        int height,
        int composerWanted,
        int overlayWanted,
        int queueWanted = 0)
    {
        width = Math.Max(width, MinWidth);
        height = Math.Max(height, MinHeight);
        var floor = MinTranscriptRows + StatusRows + 1;
        var overlay = Math.Clamp(overlayWanted, 0, Math.Max(0, height - floor));
        var queue = Math.Clamp(queueWanted, 0, Math.Min(MaxQueueRows, Math.Max(0, height - floor - overlay)));
        var composer = Math.Clamp(composerWanted, 1, MaxComposerRows);
        var transcript = height - StatusRows - overlay - queue - composer;
        if (transcript < MinTranscriptRows)
        {
            composer = Math.Max(1, height - MinTranscriptRows - StatusRows - overlay - queue);
            transcript = height - StatusRows - overlay - queue - composer;
            if (transcript < MinTranscriptRows)
            {
                queue = Math.Max(0, height - MinTranscriptRows - StatusRows - overlay - 1);
                composer = Math.Max(1, height - MinTranscriptRows - StatusRows - overlay - queue);
                transcript = height - StatusRows - overlay - queue - composer;
            }
        }

        var overlayTop = transcript;
        var statusTop = overlayTop + overlay;
        var queueTop = statusTop + StatusRows;
        var composerTop = queueTop + queue;
        return new ShellRegions(
            width,
            height,
            transcript,
            overlay,
            overlayTop,
            statusTop,
            queue,
            queueTop,
            composer,
            composerTop);
    }
}
