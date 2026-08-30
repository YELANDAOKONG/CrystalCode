using Crystal;
using Crystal.Chat;

namespace CrystalHarness.Sessions;

/// <summary>
/// Outcome of one user-message turn.
/// </summary>
public sealed record TurnResult(
    TurnStopReason StopReason,
    int ModelCallCount,
    int ToolCallCount,
    TokenUsage? Usage,
    IReadOnlyList<ChatItem> Transcript);
