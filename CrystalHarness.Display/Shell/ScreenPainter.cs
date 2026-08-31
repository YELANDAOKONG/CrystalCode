using Spectre.Console;

using CrystalHarness.Display.Composer;
using CrystalHarness.Display.Paint;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Paints one retained frame. Unchanged rows stay; a size change clears.
/// Owns cursor placement, not Live.
/// </summary>
public sealed class ScreenPainter
{
    private PaintLine[]? _previous;

    public void Clear()
    {
        _previous = null;
    }

    public void Paint(
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
        var frame = FrameRows.Assemble(regions, transcript, overlay, status, queue, composer);
        var rewriteAll = resetFrame || _previous is null || _previous.Length != frame.Count;
        var dirty = rewriteAll ? null : FrameRows.Dirty(_previous, frame);
        AnsiConsole.Cursor.Hide();
        if (rewriteAll)
        {
            AnsiConsole.Write(new ControlCode("\u001b[2J\u001b[H"));
        }

        // Wrap-off plus absolute rows: a full-width write must not scroll the frame.
        AnsiConsole.Write(new ControlCode("\u001b[?7l"));
        try
        {
            if (rewriteAll)
            {
                for (var row = 0; row < frame.Count; row++)
                {
                    WriteLine(frame[row], row, regions.Height);
                }
            }
            else
            {
                foreach (var row in dirty!)
                {
                    WriteLine(frame[row], row, regions.Height);
                }
            }
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
        _previous = [.. frame];
    }

    private static void WriteLine(PaintLine line, int row, int height)
    {
        if (row < 0 || row >= height)
        {
            return;
        }

        AnsiConsole.Write(new ControlCode($"\u001b[{row + 1};1H\u001b[2K"));
        if (!string.IsNullOrEmpty(line.Markup))
        {
            AnsiConsole.Markup(line.Markup);
        }
    }
}
