using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Providers.OpenAI;

using Xunit;

namespace CrystalCode.Providers.Tests.OpenAI;

public sealed class OpenAIProviderTests
{
    [Fact]
    public async Task CompleteAsync_OmitsAuthorizationWhenKeyIsEmpty()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(new OpenAIOptions(
            string.Empty,
            "local-model",
            new Uri("http://localhost:9000/v1/")), http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "hi")]));

        Assert.Null(handler.Request!.Headers.Authorization);
    }

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
                "gpt-5.6-sol",
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
        Assert.Equal("Crystal Code", handler.Request.Headers.UserAgent.ToString());
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
            new OpenAIOptions("test-key", "gpt-5.6-sol"),
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
            new OpenAIOptions("test-key", "gpt-5.6-sol", replayReasoningContent: true),
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

    [Fact]
    public async Task CompleteAsync_RejectsUnmatchedToolCall()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(new OpenAIOptions("test-key", "gpt-test"), http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "read"),
                new ToolCall("call_1", "read", "{}")
            ])));

        Assert.Contains("missing a tool result for 'call_1'", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }
}
