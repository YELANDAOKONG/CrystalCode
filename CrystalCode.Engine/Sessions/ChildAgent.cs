using System.Runtime.CompilerServices;

using Crystal;
using Crystal.Agents;
using Crystal.Chat;

namespace CrystalCode.Engine.Sessions;

internal sealed class ChildAgent(Func<AgentRunRequest, CancellationToken, Task<TurnResult>> run) : IAgent
{
    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        var result = await run(request, cancellationToken);
        return new AgentRunResult(request.RunId, result.Transcript,
            new AgentRunStopReason(result.StopReason.Value), result.ModelCallCount, result.ToolCallCount,
            result.AccumulatedUsage, result.StopReason == TurnStopReason.Completed ? FinishReason.Stop : null);
    }

    public async IAsyncEnumerable<AgentRunEvent> StreamAsync(AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AgentRunCompletedEvent(request.RunId, 0, await RunAsync(request, cancellationToken));
    }
}
