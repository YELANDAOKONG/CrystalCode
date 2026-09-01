namespace CrystalCode.Display.Shell;

/// <summary>
/// Splits the terminal into transcript, overlay, progress, status, queue, and composer.
/// </summary>
public static class ShellLayout
{
    public const int StatusRows = 1;

    public const int MaxComposerRows = 8;

    public const int MaxQueueRows = 8;

    public const int MaxTodoRows = 6;

    public const int MinTranscriptRows = 3;

    /// <summary>Math floor for layout measurements.</summary>
    public const int MinWidth = 16;

    /// <summary>Math floor for layout measurements.</summary>
    public const int MinHeight = 8;

    /// <summary>Smallest terminal that gets a real frame; smaller terminals get a resize notice.</summary>
    public const int MinUsableWidth = 80;

    /// <summary>Smallest terminal that gets a real frame; smaller terminals get a resize notice.</summary>
    public const int MinUsableHeight = 24;

    public static ShellRegions Measure(
        int width,
        int height,
        int composerWanted,
        int overlayWanted,
        int queueWanted = 0,
        int progressWanted = 0,
        int todoWanted = 0)
    {
        width = Math.Max(width, MinWidth);
        height = Math.Max(height, MinHeight);
        var progress = Math.Clamp(progressWanted, 0, 1);
        var chromeBase = StatusRows + progress;
        var floor = MinTranscriptRows + chromeBase + 1;
        var overlay = Math.Clamp(overlayWanted, 0, Math.Max(0, height - floor));
        var queue = Math.Clamp(queueWanted, 0, Math.Min(MaxQueueRows, Math.Max(0, height - floor - overlay)));
        var todos = Math.Clamp(
            todoWanted,
            0,
            Math.Min(MaxTodoRows, Math.Max(0, height - floor - overlay - queue)));
        var composer = Math.Clamp(composerWanted, 1, MaxComposerRows);
        var transcript = height - chromeBase - todos - overlay - queue - composer;
        if (transcript < MinTranscriptRows)
        {
            composer = Math.Max(1, height - MinTranscriptRows - chromeBase - todos - overlay - queue);
            transcript = height - chromeBase - todos - overlay - queue - composer;
            if (transcript < MinTranscriptRows)
            {
                queue = Math.Max(0, height - MinTranscriptRows - chromeBase - todos - overlay - 1);
                composer = Math.Max(1, height - MinTranscriptRows - chromeBase - todos - overlay - queue);
                transcript = height - chromeBase - todos - overlay - queue - composer;
                if (transcript < MinTranscriptRows)
                {
                    todos = Math.Max(0, height - MinTranscriptRows - chromeBase - overlay - queue - 1);
                    composer = Math.Max(1, height - MinTranscriptRows - chromeBase - todos - overlay - queue);
                    transcript = height - chromeBase - todos - overlay - queue - composer;
                }
            }
        }

        var overlayTop = transcript;
        var todoTop = overlayTop + overlay;
        var progressTop = todoTop + todos;
        var statusTop = progressTop + progress;
        var queueTop = statusTop + StatusRows;
        var composerTop = queueTop + queue;
        return new ShellRegions(
            width,
            height,
            transcript,
            overlay,
            overlayTop,
            todos,
            todoTop,
            progress,
            progressTop,
            statusTop,
            queue,
            queueTop,
            composer,
            composerTop);
    }
}
