using Crystal.Chat;
using Crystal.Tools;

namespace CrystalHarness.Sessions;

/// <summary>
/// Receives stream deltas and tool results for display.
/// </summary>
public interface ITurnObserver
{
    void OnStreamEvent(ChatStreamEvent streamEvent);

    void OnModelRoundClosed();

    void OnToolResults(IReadOnlyList<ToolResult> results);
}
