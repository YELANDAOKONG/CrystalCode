namespace CrystalCode.Engine.Sessions;

/// <summary>
/// A titled document for the transcript. This is the engine's copy of what a
/// plugin wrote, so a session event never holds a plugin contract type.
/// </summary>
public sealed record TranscriptDocument(string Title, IReadOnlyList<TranscriptDocumentBlock> Blocks)
{
    /// <summary>Returns the title and blocks as aligned plain text.</summary>
    public string ToText() => PluginDocumentText.Format(this);
}
