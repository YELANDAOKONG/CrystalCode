using CrystalCode.Display.Composer;
using CrystalCode.Display.Paint;
using Spectre.Console;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Paints one retained frame. Unchanged rows stay; a size change clears.
/// Owns cursor placement, not Live.
/// </summary>
public sealed class ScreenPainter
{
    private const string BeginSynchronizedUpdate = "\u001b[?2026h";
    private const string EndSynchronizedUpdate = "\u001b[?2026l";
    private PaintLine[]? _previous;
    private IAnsiConsole? _raster;

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
        bool resetFrame = false,
        PaintLine? progress = null,
        IReadOnlyList<PaintLine>? todos = null,
        bool showCursor = true)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(composer);
        var frame = FrameRows.Assemble(
            regions,
            transcript,
            overlay,
            status,
            queue,
            composer,
            progress,
            todos);
        // One synchronized update per frame, so a terminal that supports mode
        // 2026 presents rows and cursor together. Others ignore the mode.
        AnsiConsole.Write(new ControlCode(BeginSynchronizedUpdate));
        try
        {
            WriteFrame(frame, regions.Width, regions.Height, resetFrame);
            if (showCursor)
            {
                var cursorLine = Math.Clamp(
                    regions.ComposerTop + composer.CursorRow + 1,
                    1,
                    regions.Height);
                var cursorColumn = Math.Clamp(composer.CursorColumn + 1, 1, regions.Width);
                AnsiConsole.Cursor.SetPosition(cursorColumn, cursorLine);
                AnsiConsole.Cursor.Show();
            }
            else
            {
                AnsiConsole.Cursor.Hide();
            }
        }
        finally
        {
            AnsiConsole.Write(new ControlCode(EndSynchronizedUpdate));
        }

        _previous = [.. frame];
    }

    public void PaintFrame(IReadOnlyList<PaintLine> frame, int width, int height, bool resetFrame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        AnsiConsole.Write(new ControlCode(BeginSynchronizedUpdate));
        try
        {
            WriteFrame(frame, width, height, resetFrame);
            AnsiConsole.Cursor.Hide();
        }
        finally
        {
            AnsiConsole.Write(new ControlCode(EndSynchronizedUpdate));
        }

        _previous = [.. frame];
    }

    private void WriteFrame(IReadOnlyList<PaintLine> frame, int width, int height, bool resetFrame)
    {
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
                    WriteLine(frame[row], row, height, width);
                }
            }
            else
            {
                foreach (var row in dirty!)
                {
                    WriteLine(frame[row], row, height, width);
                }
            }
        }
        finally
        {
            AnsiConsole.Write(new ControlCode("\u001b[?7h"));
        }
    }

    private void WriteLine(PaintLine line, int row, int height, int width)
    {
        if (row < 0 || row >= height)
        {
            return;
        }

        AnsiConsole.Write(new ControlCode($"\u001b[{row + 1};1H\u001b[2K"));
        var rendered = FrameRow.Create(line, width, _raster ??= FrameRow.CreateRaster());
        if (!rendered.IsEmpty)
        {
            AnsiConsole.Write(rendered);
        }
    }
}
