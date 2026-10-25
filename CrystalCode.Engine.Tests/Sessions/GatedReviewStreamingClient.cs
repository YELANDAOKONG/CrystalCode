using System.Runtime.CompilerServices;

using Crystal;
using Crystal.Chat;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Streams scripted rounds and holds every approval review inside
/// <see cref="CompleteAsync"/> until <see cref="Release"/> supplies the verdict.
/// </summary>
internal sealed class GatedReviewStreamingClient : IStreamingChatClient
{
    private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;
    private readonly TaskCompletionSource _reviewEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _verdict =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GatedReviewStreamingClient(params IReadOnlyList<ChatStreamEvent>[] rounds)
    {
        _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
    }

    public Task ReviewEntered => _reviewEntered.Task;

    public void Release(string verdict) => _verdict.TrySetResult(verdict);

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChatStreamEvent> events;
        lock (_rounds)
        {
            events = _rounds.Count == 0 ? [] : _rounds.Dequeue();
        }
        return EnumerateAsync(events, cancellationToken);
    }

    public async Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        _reviewEntered.TrySetResult();
        var verdict = await _verdict.Task.WaitAsync(cancellationToken);
        return new ChatResponse(
        [
            new ChatCandidate([new ChatMessage(ChatRole.Assistant, verdict)], FinishReason.Stop)
        ]);
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
