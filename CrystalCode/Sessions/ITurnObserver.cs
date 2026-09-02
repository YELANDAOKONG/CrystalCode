using Crystal;
using Crystal.Chat;
using Crystal.Tools;

namespace CrystalCode.Sessions;

/// <summary>
    /// Receives stream deltas, retry waits, tool calls, tool results, and usage updates for display.
/// </summary>
public interface ITurnObserver
{
    void OnStreamEvent(ChatStreamEvent streamEvent);

    void OnRetry(SessionRetryAttempt attempt);

    void OnModelRoundClosed();

    void OnToolCalls(IReadOnlyList<ToolCall> calls);

    void OnToolResults(IReadOnlyList<ToolResult> results);

    void OnUsageUpdated(TokenUsage? contextUsage, TokenUsage? turnCumulativeUsage = null);
}
