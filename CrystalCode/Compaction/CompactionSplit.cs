using Crystal.Chat;

namespace CrystalCode.Compaction;

/// <summary>
/// Older history to summarize versus recent turns kept verbatim.
/// </summary>
public sealed record CompactionSplit(
    IReadOnlyList<ChatItem> Head,
    IReadOnlyList<ChatItem> Tail,
    string? PreviousSummary);
