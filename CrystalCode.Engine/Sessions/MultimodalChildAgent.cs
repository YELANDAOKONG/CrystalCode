using System.Runtime.CompilerServices;

using Crystal;
using Crystal.Multimodal.Agents;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Engine.Sessions;

internal sealed class MultimodalChildAgent(
    MultimodalChatCapabilities capabilities,
    Func<CancellationToken, Task<TurnResult>> run,
    Func<TurnResult, IReadOnlyList<MultimodalChatItem>> transcript) : IMultimodalAgent
{
    public MultimodalChatCapabilities Capabilities => capabilities;

    public async Task<MultimodalAgentRunResult> RunAsync(MultimodalAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await run(cancellationToken);
        return new MultimodalAgentRunResult(request.RunId, transcript(result),
            new MultimodalAgentRunStopReason(result.StopReason.Value), result.ModelCallCount, result.ToolCallCount,
            result.AccumulatedUsage, result.StopReason == TurnStopReason.Completed ? FinishReason.Stop : null);
    }

    public async IAsyncEnumerable<MultimodalAgentRunEvent> StreamAsync(MultimodalAgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new MultimodalAgentRunCompletedEvent(request.RunId, 0, await RunAsync(request, cancellationToken));
    }
}
