using Crystal;
using Crystal.Chat;

namespace CrystalHarness.Tests.Approvals;

internal sealed class FixedChatClient : IChatClient
{
    private readonly string _text;

    public FixedChatClient(string text)
    {
        _text = text;
    }

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        return Task.FromResult(
            new ChatResponse(
            [
                new ChatCandidate(
                    [new ChatMessage(ChatRole.Assistant, _text)],
                    FinishReason.Stop)
            ]));
    }

    public ChatRequest? LastRequest { get; private set; }
}
