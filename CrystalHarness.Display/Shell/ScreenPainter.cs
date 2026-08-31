using Spectre.Console;

using CrystalHarness.Display.Composer;
using CrystalHarness.Display.Paint;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Paints one retained frame. Owns cursor placement, not Live.
/// </summary>
public static class ScreenPainter
{
    public static void Paint(
        ShellRegions regions,
        IReadOnlyList<PaintLine> transcript,
        IReadOnlyList<PaintLine> overlay,
        PaintLine status,
        IReadOnlyList<PaintLine> queue,
        ComposerView composer,
        bool resetFrame = false)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(composer);
        AnsiConsole.Cursor.Hide();
        if (resetFrame)
        {
            AnsiConsole.Write(new ControlCode("\u001b[2J\u001b[H"));
        }

        // Wrap-off plus absolute rows: a full-width write must not scroll the frame.
        AnsiConsole.Write(new ControlCode("\u001b[?7l"));
        try
        {
            var row = 0;
            row = WriteBlock(transcript, regions.TranscriptRows, regions.Width, row, regions.Height);
            row = WriteBlock(overlay, regions.OverlayRows, regions.Width, row, regions.Height);
            WriteLine(status, regions.Width, row, regions.Height);
            row++;
            row = WriteBlock(queue, regions.QueueRows, regions.Width, row, regions.Height);
            WriteBlock(composer.Lines, regions.ComposerRows, regions.Width, row, regions.Height);
        }
        finally
        {
            AnsiConsole.Write(new ControlCode("\u001b[?7h"));
        }

        var cursorLine = Math.Clamp(
            regions.ComposerTop + composer.CursorRow + 1,
            1,
            regions.Height);
        var cursorColumn = Math.Clamp(composer.CursorColumn + 1, 1, regions.Width);
        AnsiConsole.Cursor.SetPosition(cursorColumn, cursorLine);
        AnsiConsole.Cursor.Show();
    }

    private static int WriteBlock(
        IReadOnlyList<PaintLine> lines,
        int rows,
        int width,
        int row,
        int height)
    {
        for (var i = 0; i < rows; i++)
        {
            var line = i < lines.Count ? lines[i] : PaintLine.Blank;
            WriteLine(line, width, row, height);
            row++;
        }

        return row;
    }

    private static void WriteLine(PaintLine line, int width, int row, int height)
    {
        if (row < 0 || row >= height)
        {
            return;
        }

        AnsiConsole.Write(new ControlCode($"\u001b[{row + 1};1H\u001b[2K"));
        var fitted = line.Fit(width);
        if (!string.IsNullOrEmpty(fitted.Markup))
        {
            AnsiConsole.Markup(fitted.Markup);
        }
    }
}
