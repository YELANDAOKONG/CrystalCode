namespace CrystalCode.Engine.Events;

/// <summary>
/// A context compaction ended. <see cref="Status"/> is <c>completed</c> when
/// the transcript was folded, <c>unchanged</c> when there was nothing to
/// fold, <c>failed</c> when it could not be reduced, and <c>cancelled</c>.
/// <see cref="Summary"/> is the stored summary, set only when the operator's
/// summary display setting shows it for this kind of compaction.
/// </summary>
public sealed record CompactionFinished(string Id, string Status, string? Summary) : SessionEvent;
