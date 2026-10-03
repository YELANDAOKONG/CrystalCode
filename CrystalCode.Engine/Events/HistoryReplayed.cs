using Crystal.Chat;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The visible conversation was replaced by a saved transcript.
/// </summary>
public sealed record HistoryReplayed(IReadOnlyList<ChatItem> Items) : SessionEvent;
