using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A titled document from a plugin slash command, for the transcript.
/// </summary>
public sealed record DocumentWritten(TranscriptDocument Document) : SessionEvent;
