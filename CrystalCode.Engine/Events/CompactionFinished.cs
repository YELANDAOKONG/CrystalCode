namespace CrystalCode.Engine.Events;

/// <summary>
/// A context compaction ended. <see cref="Status"/> is <c>completed</c> when
/// the transcript was folded, <c>unchanged</c> when there was nothing to
/// fold, <c>failed</c> when it could not be reduced, and <c>cancelled</c>.
/// <see cref="Summary"/> is the stored summary of a completed compaction
/// that wrote one; it is part of the session record and is also archived.
/// <see cref="Shown"/> is the operator's summary display setting for this
/// kind of compaction and decides only whether a front end draws it.
/// </summary>
public sealed record CompactionFinished(string Id, string Status, string? Summary, bool Shown = false) : SessionEvent;
