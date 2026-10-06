using CrystalCode.Display.Transcript;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Keeps the transcript on the row the operator is reading. The scroll
/// distance is measured from the bottom, and the anchored row is one
/// committed line. New rows at the end move that distance so the line stays
/// put. At the bottom nothing is anchored, so the view follows new output,
/// and a scroll that leaves the bottom is measured from the latest row count.
/// </summary>
public sealed class ScrollAnchor
{
    private int _width;
    private int _applied = -1;
    private ScrollMark? _mark;

    /// <summary>
    /// Returns the distance from the bottom for this paint. A width change
    /// reflows every row, so the requested distance is kept and a new anchor
    /// is taken. An explicit return to the bottom clears the anchor.
    /// </summary>
    public int Resolve(int width, TranscriptLog log, int viewportRows, int scrollBack)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (viewportRows < 0)
        {
            viewportRows = 0;
        }

        var count = log.RowCount(width);
        var maxScroll = Math.Max(0, count - viewportRows);
        var requested = Math.Max(0, scrollBack);
        if (_applied < 0 || width != _width)
        {
            return Adopt(width, log, viewportRows, count, Math.Clamp(requested, 0, maxScroll));
        }

        if (requested == 0)
        {
            return Adopt(width, log, viewportRows, count, 0);
        }

        if (_mark is not { } mark)
        {
            return Adopt(width, log, viewportRows, count, Math.Clamp(requested, 0, maxScroll));
        }

        var wheel = requested - _applied;
        var preservedStart = log.RowOf(width, mark);
        var lastStart = Math.Max(0, count - 1);
        var wheeledStart = Math.Clamp(preservedStart - wheel, 0, lastStart);
        var distance = count - viewportRows - wheeledStart;
        if (distance < 0)
        {
            distance = 0;
        }

        return Adopt(width, log, viewportRows, count, Math.Clamp(distance, 0, maxScroll));
    }

    public void Reset()
    {
        _width = 0;
        _applied = -1;
        _mark = null;
    }

    private int Adopt(
        int width,
        TranscriptLog log,
        int viewportRows,
        int count,
        int distance)
    {
        var maxScroll = Math.Max(0, count - viewportRows);
        distance = Math.Clamp(distance, 0, maxScroll);
        _width = width;
        _applied = distance;
        if (distance == 0 || count == 0)
        {
            _mark = null;
            return distance;
        }

        var contentStart = Math.Max(0, count - viewportRows - distance);
        _mark = log.MarkAt(width, contentStart);
        return distance;
    }
}
