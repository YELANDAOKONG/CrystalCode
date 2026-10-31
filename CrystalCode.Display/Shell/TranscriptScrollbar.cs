using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Draws a one-column scrollbar at the right edge of the transcript rows, with
/// one blank column before it so wrapped text does not touch the bar. Both
/// columns are always reserved so wrapping does not change when the bar
/// appears; the bar stays blank while everything fits on one screen.
/// </summary>
public static class TranscriptScrollbar
{
    private const string Track = "│";
    private const string Thumb = "┃";

    /// <summary>Columns the bar takes from the transcript width: one gap and the bar.</summary>
    public const int Columns = 2;

    /// <summary>
    /// Thumb rows within a viewport of <paramref name="rows"/>, or null when
    /// <paramref name="total"/> rows fit. <paramref name="scrollBack"/> counts rows from the bottom.
    /// </summary>
    public static (int Start, int Length)? ThumbOf(int total, int rows, int scrollBack)
    {
        if (rows < 1 || total <= rows)
        {
            return null;
        }

        var length = Math.Clamp((int)Math.Round((double)rows * rows / total), 1, rows);
        var maxScroll = total - rows;
        var fromTop = maxScroll - Math.Clamp(scrollBack, 0, maxScroll);
        var start = (int)Math.Round((double)fromTop / maxScroll * (rows - length));
        return (start, length);
    }

    /// <summary>
    /// Like <see cref="ThumbOf"/> for a window that starts <paramref name="first"/> rows below the top.
    /// </summary>
    public static (int Start, int Length)? ThumbFromTop(int total, int rows, int first) =>
        ThumbOf(total, rows, Math.Max(0, total - rows) - Math.Clamp(first, 0, Math.Max(0, total - rows)));

    /// <summary>
    /// The bar cell for <paramref name="row"/>: a space while there is no thumb,
    /// otherwise the thumb or the track.
    /// </summary>
    public static PaintLine Cell(int row, (int Start, int Length)? thumb)
    {
        var trackColor = Theme.ForStatus("scrollbar", Theme.Rule);
        var thumbColor = Theme.ForStatus("scrollbar-thumb", Theme.Muted);
        if (thumb is not { } t)
        {
            return new PaintLine(" ", " ");
        }

        return row >= t.Start && row < t.Start + t.Length
            ? new PaintLine($"[{thumbColor}]{Thumb}[/]", Thumb)
            : new PaintLine($"[{trackColor}]{Track}[/]", Track);
    }

    /// <summary>
    /// Returns <paramref name="lines"/> fitted to <paramref name="width"/> minus the bar column,
    /// each followed by its bar cell.
    /// </summary>
    public static IReadOnlyList<PaintLine> Apply(
        IReadOnlyList<PaintLine> lines,
        int width,
        int total,
        int scrollBack)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var body = width - Columns;
        if (body < 1)
        {
            return lines;
        }

        var thumb = ThumbOf(total, lines.Count, scrollBack);
        var result = new PaintLine[lines.Count];
        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row].Fit(body);
            var pad = new string(' ', body - TextWidth.Measure(line.Plain) + Columns - 1);
            var cell = Cell(row, thumb);
            result[row] = new PaintLine(line.Markup + pad + cell.Markup, line.Plain + pad + cell.Plain);
        }

        return result;
    }
}
