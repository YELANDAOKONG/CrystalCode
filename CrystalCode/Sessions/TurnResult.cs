using Crystal;
using Crystal.Chat;

namespace CrystalCode.Sessions;

/// <summary>
/// Outcome of one user-message turn, including last-request and accumulated usage.
/// </summary>
public sealed record TurnResult(
    TurnStopReason StopReason,
    int ModelCallCount,
    int ToolCallCount,
    TokenUsage? Usage,
    IReadOnlyList<ChatItem> Transcript,
    TokenUsage? AccumulatedUsage = null);
