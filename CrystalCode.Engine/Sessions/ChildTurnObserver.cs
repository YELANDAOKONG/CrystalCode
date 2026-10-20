using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Sessions;

internal sealed class ChildTurnObserver(
    Action<SessionEvent> publish,
    Action<TokenUsage?, TokenUsage?>? usage = null,
    Action<int>? estimated = null) : ITurnObserver
{
    public void OnStreamEvent(ChatStreamEvent streamEvent) => publish(new StreamReceived(streamEvent));
    public void OnRetry(SessionRetryAttempt attempt) => publish(new RetryScheduled(attempt));
    public void OnModelRoundClosed() => publish(new ModelRoundClosed());
    public void OnToolCalls(IReadOnlyList<ToolCall> calls) => publish(new ToolCallsIssued(calls));
    public void OnToolResults(IReadOnlyList<ToolResult> results) => publish(new ToolResultsReceived(results));
    public void OnContextEstimated(int tokens)
    {
        estimated?.Invoke(tokens);
        publish(new ContextEstimated(tokens));
    }

    public void OnFault(string message) => publish(new ErrorWritten(message));
    public void OnUsageUpdated(TokenUsage? contextUsage, TokenUsage? turnCumulativeUsage = null) =>
        usage?.Invoke(contextUsage, turnCumulativeUsage);
}
