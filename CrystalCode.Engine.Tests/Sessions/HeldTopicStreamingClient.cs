using Crystal;
using Crystal.Chat;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Answers turn requests at once and holds every topic-naming request until
/// released, so a turn can run while naming is still open.
/// </summary>
internal sealed class HeldTopicStreamingClient : IStreamingChatClient
{
    private readonly TaskCompletionSource _topicStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _topic;
    private int _turns;

    public HeldTopicStreamingClient(string topic)
    {
        _topic = topic;
    }

    public Task TopicStarted => _topicStarted.Task;

    public int Turns => Volatile.Read(ref _turns);

    public void Release() => _release.TrySetResult();

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        EnumerateAsync(request, cancellationToken);

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("StreamingTurn uses StreamAsync.");

    private async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var naming = request.Items.Count > 0
            && request.Items[0] is ChatMessage { Role.Value: "system" } system
            && system.Text.Contains("concise topic name", StringComparison.Ordinal);
        if (naming)
        {
            _topicStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            yield return new ChatTextDelta(0, 0, ChatRole.Assistant, _topic);
        }
        else
        {
            Interlocked.Increment(ref _turns);
            yield return new ChatTextDelta(0, 0, ChatRole.Assistant, "done");
        }

        yield return new ChatUsageReceived(new TokenUsage(10, 2, 1));
        yield return new ChatCandidateCompleted(0, FinishReason.Stop);
    }
}
