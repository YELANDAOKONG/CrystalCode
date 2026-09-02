using Spectre.Console.Rendering;

namespace CrystalCode.Display.Transcript;

/// <summary>
/// One committed transcript block.
/// </summary>
public sealed record TranscriptEntry(
    TranscriptKind Kind,
    string Text,
    IRenderable? Widget = null,
    string? ToolName = null);
