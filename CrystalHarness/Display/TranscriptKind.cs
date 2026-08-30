namespace CrystalHarness.Display;

/// <summary>
/// One visual role in the session transcript.
/// </summary>
public enum TranscriptKind
{
    User,
    Assistant,
    Thinking,
    Tool,
    Result,
    Note,
    Error,
    Approval
}
