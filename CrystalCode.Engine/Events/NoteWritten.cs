namespace CrystalCode.Engine.Events;

/// <summary>
/// Plain informational text for the transcript.
/// </summary>
public sealed record NoteWritten(string Text) : SessionEvent;
