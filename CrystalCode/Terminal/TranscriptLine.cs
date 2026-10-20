using CrystalCode.Display.Transcript;

namespace CrystalCode.Terminal;

/// <summary>
/// One replay row for the transcript viewport.
/// </summary>
public sealed record TranscriptLine(
    TranscriptKind Kind,
    string Text,
    string? ToolName = null);
