namespace CrystalHarness.Display;

/// <summary>
/// One committed transcript block.
/// </summary>
public sealed record TranscriptEntry(TranscriptKind Kind, string Text);
