namespace CrystalHarness.Display;

/// <summary>
/// Wrapped composer rows and the cursor inside that window.
/// </summary>
public sealed record ComposerView(
    IReadOnlyList<PaintLine> Lines,
    int CursorRow,
    int CursorColumn);
