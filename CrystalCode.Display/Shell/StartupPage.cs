using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Shell;

/// <summary>
/// The full-screen page shown while a session starts, before the session
/// frame and its composer exist. It lists the loading phase and the items
/// that started loading, newest last, and keeps the latest rows when the
/// list is taller than the screen.
/// </summary>
public static class StartupPage
{
    public const string Title = "Starting Session";

    public const string Hint = "Ctrl+C Cancel";

    /// <summary>Rows above the item list: title, blank, phase, blank.</summary>
    private const int HeaderRows = 4;

    /// <summary>Rows below the item list: blank, hint.</summary>
    private const int FooterRows = 2;

    public static IReadOnlyList<PaintLine> Paint(
        int width,
        int height,
        string spinner,
        string phase,
        IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(spinner);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(items);
        var rows = Math.Max(1, height);
        var frame = new PaintLine[rows];
        Array.Fill(frame, PaintLine.Blank);
        if (width < 1)
        {
            return frame;
        }

        var lines = new List<PaintLine>
        {
            PaintLine.Colored(Theme.Heading, "  " + Title),
            PaintLine.Blank,
            PaintLine.Colored(
                Theme.Accent,
                "  " + spinner + "  " + (phase.Length == 0 ? "Preparing" : phase)),
            PaintLine.Blank
        };
        var room = Math.Max(0, rows - HeaderRows - FooterRows);
        var shown = items.Count <= room ? items : items.Skip(items.Count - room).ToArray();
        foreach (var item in shown)
        {
            lines.Add(PaintLine.Colored(Theme.Muted, "     " + TerminalText.SanitizeLine(item)));
        }

        lines.Add(PaintLine.Blank);
        lines.Add(PaintLine.Colored(Theme.Muted, "  " + Hint));
        for (var row = 0; row < rows && row < lines.Count; row++)
        {
            frame[row] = lines[row].Fit(width);
        }

        return frame;
    }
}
