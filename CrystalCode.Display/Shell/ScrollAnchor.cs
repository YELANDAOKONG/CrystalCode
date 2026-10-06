namespace CrystalCode.Display.Shell;

/// <summary>
/// Keeps the transcript where the operator left it. The scroll position is a
/// distance from the bottom, so rows that arrive while the operator reads
/// earlier output would push that output away. Each paint reports the total
/// row count; growth at the same width is added to the distance. At the bottom
/// (distance zero) the view keeps following new rows.
/// </summary>
public sealed class ScrollAnchor
{
    private int _width;
    private int _rowCount = -1;

    /// <summary>
    /// Returns the distance from the bottom after rows were added or removed
    /// since the previous call. A width change re-wraps every row, so the
    /// distance is kept as it is.
    /// </summary>
    public int Resolve(int width, int rowCount, int scrollBack)
    {
        var previousWidth = _width;
        var previousCount = _rowCount;
        _width = width;
        _rowCount = rowCount;
        if (scrollBack > 0
            && previousCount >= 0
            && previousWidth == width
            && rowCount > previousCount)
        {
            return scrollBack + (rowCount - previousCount);
        }

        return scrollBack;
    }

    public void Reset()
    {
        _width = 0;
        _rowCount = -1;
    }
}
