using Crystal.Chat;
using Crystal.Reasoning;

using CrystalHarness.Providers.OpenAI;

using Xunit;

namespace CrystalHarness.Providers.Tests.OpenAI;

public sealed class OpenAIProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesMaxCompletionTokensAndOrganization()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "done" },
                      "finish_reason": "stop"
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(
            new OpenAIOptions(
                "test-key",
                "gpt-5",
                organization: "org_test",
                maxTokens: 256,
                temperature: 0.2),
            http);

        var response = await provider.CompleteAsync(
            new ChatRequest(
                [new ChatMessage(ChatRole.User, "Hi")],
                reasoning: new ReasoningOptions(effort: ReasoningEffort.Minimal)));

        Assert.NotNull(handler.Request);
        Assert.Equal(
            new Uri("https://api.openai.com/v1/chat/completions"),
            handler.Request.RequestUri);
        Assert.Equal("org_test", handler.Request.Headers.GetValues("OpenAI-Organization").Single());
        Assert.Contains("\"max_completion_tokens\":256", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"max_tokens\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"reasoning_effort\":\"minimal\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"thinking\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal("done", Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]).Text);
    }

    [Fact]
    public async Task CompleteAsync_RejectsReasoningReplayByDefault()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "done" },
                      "finish_reason": "stop"
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(
            new OpenAIOptions("test-key", "gpt-5"),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(
                new ChatRequest(
                [
                    new ChatMessage(ChatRole.User, "Hi"),
                    new ChatReasoningItem(
                        new ReasoningContent(
                            [new ReasoningText("thought", ReasoningTextKind.Trace)]))
                ])));

        Assert.Contains("cannot replay reasoning", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_WritesReasoningContentWhenReplayEnabled()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "done" },
                      "finish_reason": "stop"
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(
            new OpenAIOptions("test-key", "gpt-5", replayReasoningContent: true),
            http);

        await provider.CompleteAsync(
            new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "Hi"),
                new ChatReasoningItem(
                    new ReasoningContent(
                        [new ReasoningText("thought", ReasoningTextKind.Trace)])),
                new ChatMessage(ChatRole.Assistant, "earlier")
            ]));

        Assert.Contains("\"reasoning_content\":\"thought\"", handler.Body, StringComparison.Ordinal);
    }
}
