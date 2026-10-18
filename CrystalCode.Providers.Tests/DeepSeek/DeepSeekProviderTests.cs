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
                [ToolDefinition.Json("read", schema.RootElement, "Read a file.")],
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
    public async Task CompleteAsync_ForwardsMaximumAndOtherEfforts()
    {
        var maximum = await SendEffort(ReasoningEffort.Maximum);
        Assert.Contains("\"reasoning_effort\":\"maximum\"", maximum, StringComparison.Ordinal);

        var max = await SendEffort(new ReasoningEffort("max"));
        Assert.Contains("\"reasoning_effort\":\"max\"", max, StringComparison.Ordinal);

        var custom = await SendEffort(new ReasoningEffort("xhigh"));
        Assert.Contains("\"reasoning_effort\":\"xhigh\"", custom, StringComparison.Ordinal);

        var minimal = await SendEffort(ReasoningEffort.Minimal);
        Assert.Contains("\"reasoning_effort\":\"minimal\"", minimal, StringComparison.Ordinal);

        static async Task<string?> SendEffort(ReasoningEffort effort)
        {
            var handler = new RecordingHandler(
                JsonResponse.Create(
                    """
                    {
                      "choices": [
                        {
                          "message": { "role": "assistant", "content": "ok" },
                          "finish_reason": "stop"
                        }
                      ]
                    }
                    """));
            using var http = new HttpClient(handler);
            using var provider = new DeepSeekProvider(
                new DeepSeekOptions("test-key", "deepseek-v4-flash"),
                http);
            await provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "Hi")],
                reasoning: new ReasoningOptions(ReasoningMode.Enabled, effort)));
            return handler.Body;
        }
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
                JsonToolCall("call_dangling", "bash", "{\"command\":\"ls\"}"),
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
    public async Task StreamAsync_ThrowsErrorEventChunk()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"error","code":"insufficient_quota","message":"quota exhausted"}

            """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => CollectAsync(provider));

        Assert.Equal("insufficient_quota", exception.ErrorCode);
        Assert.Contains("quota exhausted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ThrowsJsonErrorBody()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"error":{"code":"invalid_request_error","message":"too many images"}}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => CollectAsync(provider));

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ReportsBodyWithoutEventStream()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => CollectAsync(provider));

        Assert.Contains(
            "did not contain an event stream",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ParsesSseBodyMislabeledAsJson()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"hello"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var events = await CollectAsync(provider);

        Assert.Contains(
            events,
            item => item is ChatTextDelta { Text: "hello" });
        Assert.Contains(events, item => item is ChatCandidateCompleted);
    }

    [Fact]
    public async Task StreamAsync_IgnoresNullError()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"hello"},"finish_reason":null}],"error":null}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var events = await CollectAsync(provider);

        Assert.Contains(
            events,
            item => item is ChatTextDelta { Text: "hello" });
    }

    [Fact]
    public async Task CompleteAsync_MapsErrorBodyWithOkStatus()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"error":{"code":"invalid_request_error","message":"too many images"}}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => provider.CompleteAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "Hello.")])));

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Null(exception.StatusCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<List<ChatStreamEvent>> CollectAsync(DeepSeekProvider provider)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in provider.StreamAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hello.")])))
        {
            events.Add(streamEvent);
        }

        return events;
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

    [Fact]
    public async Task CompleteAsync_ReplaysReasoningOnTheToolCallMessage()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"choices":[{"message":{"role":"assistant","content":"four"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-flash"),
            http);
        var reasoning = new ChatReasoningItem(new ReasoningContent(
            [new ReasoningText("look at all four", ReasoningTextKind.Trace)],
            new OpaqueReasoningState(
                DeepSeekProvider.ReasoningStateFormat,
                Encoding.UTF8.GetBytes("look at all four"))));

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Inspect the screenshots."),
            reasoning,
            JsonToolCall("call_1", "view_image", """{"path":"1.png"}"""),
            JsonToolCall("call_2", "view_image", """{"path":"2.png"}"""),
            JsonToolCall("call_3", "view_image", """{"path":"3.png"}"""),
            JsonToolCall("call_4", "view_image", """{"path":"4.png"}"""),
            new ToolResult("call_1", "Image loaded."),
            new ToolResult("call_2", "Image loaded."),
            new ToolResult("call_3", "Image loaded."),
            new ToolResult("call_4", "Image loaded."),
            new ChatMessage(ChatRole.User, "What did you see?")
        ]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var assistant = sent.RootElement.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "assistant");
        Assert.Equal("look at all four", assistant.GetProperty("reasoning_content").GetString());
        Assert.Equal(4, assistant.GetProperty("tool_calls").GetArrayLength());
    }
}
