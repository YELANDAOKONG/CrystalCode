using Spectre.Console.Rendering;

namespace CrystalHarness.Display.Transcript;

/// <summary>
/// One committed transcript block.
/// </summary>
public sealed record TranscriptEntry(
    TranscriptKind Kind,
    string Text,
    IRenderable? Widget = null);
