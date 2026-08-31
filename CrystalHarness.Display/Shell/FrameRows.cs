using CrystalHarness.Display.Composer;
using CrystalHarness.Display.Paint;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Builds one full-height frame and names the rows that changed.
/// </summary>
public static class FrameRows
{
    public static IReadOnlyList<PaintLine> Assemble(
        ShellRegions regions,
        IReadOnlyList<PaintLine> transcript,
        IReadOnlyList<PaintLine> overlay,
        PaintLine status,
        IReadOnlyList<PaintLine> queue,
        ComposerView composer)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(composer);
        var lines = new PaintLine[regions.Height];
        var row = 0;
        row = CopyBlock(lines, transcript, regions.TranscriptRows, regions.Width, row, regions.Height);
        row = CopyBlock(lines, overlay, regions.OverlayRows, regions.Width, row, regions.Height);
        if (row >= 0 && row < regions.Height)
        {
            lines[row] = status.Fit(regions.Width);
        }

        row++;
        row = CopyBlock(lines, queue, regions.QueueRows, regions.Width, row, regions.Height);
        CopyBlock(lines, composer.Lines, regions.ComposerRows, regions.Width, row, regions.Height);
        return lines;
    }

    public static IReadOnlyList<int> Dirty(
        IReadOnlyList<PaintLine>? previous,
        IReadOnlyList<PaintLine> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null || previous.Count != current.Count)
        {
            var all = new int[current.Count];
            for (var i = 0; i < all.Length; i++)
            {
                all[i] = i;
            }

            return all;
        }

        var dirty = new List<int>();
        for (var i = 0; i < current.Count; i++)
        {
            if (previous[i] != current[i])
            {
                dirty.Add(i);
            }
        }

        return dirty;
    }

    private static int CopyBlock(
        PaintLine[] lines,
        IReadOnlyList<PaintLine> source,
        int rows,
        int width,
        int row,
        int height)
    {
        for (var i = 0; i < rows; i++)
        {
            if (row >= 0 && row < height)
            {
                var line = i < source.Count ? source[i] : PaintLine.Blank;
                lines[row] = line.Fit(width);
            }

            row++;
        }

        return row;
    }
}
