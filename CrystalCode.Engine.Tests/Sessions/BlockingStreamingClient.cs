using System.Runtime.CompilerServices;

using Crystal.Chat;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Streams one delta and then waits until the request is cancelled, so a test
/// can interrupt a turn that is genuinely in flight.
/// </summary>
internal sealed class BlockingStreamingClient : IStreamingChatClient
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        return EnumerateAsync(cancellationToken);
    }

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("StreamingTurn uses StreamAsync.");
    }

    private async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatTextDelta(0, 0, ChatRole.Assistant, "working");
        _started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
