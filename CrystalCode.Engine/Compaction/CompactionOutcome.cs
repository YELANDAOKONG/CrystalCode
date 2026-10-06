using Crystal.Chat;

namespace CrystalCode.Engine.Compaction;

/// <summary>
/// Transcript after an attempted compaction. <see cref="CompactionOutcome.Summary"/>
/// is the stored summary when a new one was written.
/// </summary>
public sealed record CompactionOutcome(
    IReadOnlyList<ChatItem> Transcript,
    CompactionKind Kind,
    string? Summary = null)
{
    public bool Compacted => Kind == CompactionKind.Applied;
}
