using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Compaction;

namespace CrystalCode.Engine.Sessions.Interfaces;
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

    void OnContextEstimated(int tokens) { }

    /// <summary>
    /// A finished round reported usage. The host sizes later compaction checks
    /// from this report plus the items added after it.
    /// </summary>
    void OnContextAnchored(ContextAnchor anchor) { }

    void OnFault(string message);
}
