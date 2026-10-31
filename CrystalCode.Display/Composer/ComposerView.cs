using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Composer;

/// <summary>
/// Wrapped composer rows and the cursor inside that window.
/// <paramref name="TotalRows"/> is the wrapped row count before the window cut
/// it, and <paramref name="FirstRow"/> is the first row shown; the total is 0
/// when the projection does not report it.
/// </summary>
public sealed record ComposerView(
    IReadOnlyList<PaintLine> Lines,
    int CursorRow,
    int CursorColumn,
    int TotalRows = 0,
    int FirstRow = 0);
