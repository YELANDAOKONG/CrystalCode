using Spectre.Console;

namespace CrystalHarness.Display;

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
        ComposerView composer)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(composer);
        AnsiConsole.Cursor.Hide();
        AnsiConsole.Write(new ControlCode("\u001b[H"));

        var row = 0;
        row = WriteBlock(transcript, regions.TranscriptRows, regions.Width, row, regions.Height);
        row = WriteBlock(overlay, regions.OverlayRows, regions.Width, row, regions.Height);
        WriteLine(status, regions.Width, row, regions.Height);
        row++;
        row = WriteBlock(queue, regions.QueueRows, regions.Width, row, regions.Height);
        WriteBlock(composer.Lines, regions.ComposerRows, regions.Width, row, regions.Height);

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
        var plain = line.Plain;
        var measured = TextWidth.Measure(plain);
        if (measured > width)
        {
            plain = TextWidth.Truncate(plain, width);
            line = PaintLine.Colored(Theme.Chrome, plain);
            measured = TextWidth.Measure(plain);
        }

        var pad = Math.Max(0, width - measured);
        if (string.IsNullOrEmpty(line.Markup))
        {
            Console.Write(new string(' ', width));
        }
        else
        {
            AnsiConsole.Markup(line.Markup);
            if (pad > 0)
            {
                Console.Write(new string(' ', pad));
            }
        }

        if (row < height - 1)
        {
            Console.WriteLine();
        }
    }
}
