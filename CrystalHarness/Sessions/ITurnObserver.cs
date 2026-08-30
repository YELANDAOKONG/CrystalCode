using Crystal;
using Crystal.Chat;
using Crystal.Tools;

namespace CrystalHarness.Sessions;

/// <summary>
/// Receives stream deltas, tool results, and usage updates for display.
/// </summary>
public interface ITurnObserver
{
    void OnStreamEvent(ChatStreamEvent streamEvent);

    void OnModelRoundClosed();

    void OnToolResults(IReadOnlyList<ToolResult> results);

    void OnUsageUpdated(TokenUsage? usage);
}
