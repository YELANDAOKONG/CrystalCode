using System.Runtime.CompilerServices;

using Crystal.Chat;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Completes any scripted rounds, then waits inside the next stream until
/// cancelled. <see cref="Entered"/> is set before that wait, with no delta.
/// </summary>
internal sealed class HeldStreamingClient : IStreamingChatClient
{
    private readonly Queue<IReadOnlyList<ChatStreamEvent>> _ready;
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HeldStreamingClient(params IReadOnlyList<ChatStreamEvent>[] ready)
    {
        _ready = new Queue<IReadOnlyList<ChatStreamEvent>>(ready);
    }

    public Task Entered => _entered.Task;

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_ready.Count > 0)
        {
            return EnumerateAsync(_ready.Dequeue(), cancellationToken);
        }

        return WaitAsync(cancellationToken);
    }

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("StreamingTurn uses StreamAsync.");
    }

    private async IAsyncEnumerable<ChatStreamEvent> WaitAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private static async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
        IReadOnlyList<ChatStreamEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var streamEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return streamEvent;
            await Task.Yield();
        }
    }
}
