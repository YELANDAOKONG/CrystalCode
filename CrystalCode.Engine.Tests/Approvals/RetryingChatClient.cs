using Crystal;
using Crystal.Chat;

namespace CrystalCode.Engine.Tests.Approvals;

internal sealed class RetryingChatClient : IChatClient
{
    private readonly Exception _failure;
    private readonly int _failures;
    private readonly string _text;

    public RetryingChatClient(Exception failure, int failures, string text)
    {
        _failure = failure;
        _failures = failures;
        _text = text;
    }

    public int Calls { get; private set; }

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        if (Calls <= _failures)
        {
            throw _failure;
        }

        return Task.FromResult(
            new ChatResponse(
            [
                new ChatCandidate(
                    [new ChatMessage(ChatRole.Assistant, _text)],
                    FinishReason.Stop)
            ]));
    }
}
