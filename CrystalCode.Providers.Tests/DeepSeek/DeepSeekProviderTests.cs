using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Providers.DeepSeek;

using Xunit;

namespace CrystalCode.Providers.Tests.DeepSeek;

public sealed class DeepSeekProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesChatCompletionsBody()
    {
        using var schema = JsonDocument.Parse(
            """
            {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
            """);
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "ok" },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": { "prompt_tokens": 3, "completion_tokens": 1 }
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash", maxTokens: 128),
            http);

        var response = await provider.CompleteAsync(
            new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, "Be brief."),
                    new ChatMessage(ChatRole.User, "Hello.")
                ],
                [new ToolDefinition("read", schema.RootElement, "Read a file.")],
                new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High)));

        Assert.NotNull(handler.Request);
        Assert.Equal(
            new Uri("https://api.deepseek.com/chat/completions"),
            handler.Request.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("Crystal Code", handler.Request.Headers.UserAgent.ToString());
        Assert.Contains("\"model\":\"deepseek-v4-flash\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":128", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"thinking\":{\"type\":\"enabled\"}", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"reasoning_effort\":\"high\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"read\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal("ok", Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]).Text);
        Assert.Equal(3, response.Usage?.InputTokenCount);
        Assert.Equal(1, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task CompleteAsync_RejectsUnmatchedToolCall()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.System, "system"),
                new ToolCall("call_dangling", "bash", "{\"command\":\"ls\"}"),
                new ChatMessage(ChatRole.User, "next user message")
            ])));

        Assert.Contains(
            "missing a tool result for 'call_dangling'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysOwnReasoningContent()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hello."),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("thought", ReasoningTextKind.Trace)],
                new OpaqueReasoningState(
                    DeepSeekProvider.ReasoningStateFormat,
                    Encoding.UTF8.GetBytes("thought")))),
            new ChatMessage(ChatRole.Assistant, "earlier")
        ]));

        Assert.Contains("\"reasoning_content\":\"thought\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"earlier\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysForeignReasoningAsAssistantText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hello."),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("thought", ReasoningTextKind.Trace)],
                new OpaqueReasoningState(
                    "openai.reasoning_content",
                    Encoding.UTF8.GetBytes("thought")))),
            new ChatMessage(ChatRole.Assistant, "earlier")
        ]));

        Assert.Contains("\"content\":\"thought\\n\\nearlier\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reasoning_content\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_KeepsReasoningAfterAssistantTextInOrder()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hello."),
            new ChatMessage(ChatRole.Assistant, "answer"),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("thought", ReasoningTextKind.Trace)],
                new OpaqueReasoningState(
                    "openai.reasoning_content",
                    Encoding.UTF8.GetBytes("thought"))))
        ]));

        Assert.Contains("\"content\":\"answer\\n\\nthought\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reasoning_content\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_OmitsForeignReasoningWithoutReadableText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hello."),
            new ChatReasoningItem(new ReasoningContent(
                state: new OpaqueReasoningState(
                    "gemini.part",
                    Encoding.UTF8.GetBytes("""{"functionCall":{"name":"read"}}"""))))
        ]));

        Assert.DoesNotContain("\"reasoning_content\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains(
            "\"messages\":[{\"role\":\"user\",\"content\":\"Hello.\"}]",
            handler.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_MapsHttpErrorToDeepSeekException()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {"error":{"code":"invalid_request","message":"bad model"}}
                """,
                HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => provider.CompleteAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "Hello.")])));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_request", exception.ErrorCode);
        Assert.Null(exception.RetryAfter);
        Assert.Contains("bad model", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_MapsRetryAfterAndErrorCode()
    {
        var response = JsonResponse.Create(
            """
            {"error":{"code":"rate_limit_exceeded","message":"slow down"}}
            """,
            HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(8));
        var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => provider.CompleteAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "Hello.")])));

        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("rate_limit_exceeded", exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(8), exception.RetryAfter);
    }

    [Fact]
    public async Task CompleteAsync_RejectsJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "json")],
                jsonOutput: JsonResponse.OutputSchema())));

        Assert.Contains("does not support a JSON output schema", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }
}
