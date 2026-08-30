using Crystal.Chat;

namespace CrystalHarness.Tests.Sessions;

internal sealed class ScriptedStreamingClient : IStreamingChatClient
{
    private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;

    public ScriptedStreamingClient(params IReadOnlyList<ChatStreamEvent>[] rounds)
    {
        _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
    }

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        RequestCount++;
        var events = _rounds.Count == 0
            ? Array.Empty<ChatStreamEvent>()
            : _rounds.Dequeue();
        return EnumerateAsync(events, cancellationToken);
    }

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("StreamingTurn uses StreamAsync.");
    }

    public ChatRequest? LastRequest { get; private set; }

    public int RequestCount { get; private set; }

    private static async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
        IReadOnlyList<ChatStreamEvent> events,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        foreach (var streamEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return streamEvent;
            await Task.Yield();
        }
    }
}
