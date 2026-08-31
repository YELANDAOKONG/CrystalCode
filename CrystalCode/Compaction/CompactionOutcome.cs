using Crystal.Chat;

namespace CrystalCode.Compaction;

/// <summary>
/// Transcript after an attempted compaction.
/// </summary>
public sealed record CompactionOutcome(
    IReadOnlyList<ChatItem> Transcript,
    CompactionKind Kind)
{
    public bool Compacted => Kind == CompactionKind.Applied;
}
