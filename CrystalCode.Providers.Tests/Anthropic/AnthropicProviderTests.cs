using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Anthropic;

using Xunit;

namespace CrystalCode.Providers.Tests.Anthropic;

public sealed class AnthropicProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesMessagesContractAndReadsToolUse()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"role":"assistant","content":[
              {"type":"thinking","thinking":"plan","signature":"signed"},
              {"type":"tool_use","id":"toolu_1","name":"read","input":{"path":"a"}}
            ],"stop_reason":"tool_use","usage":{"input_tokens":12,"output_tokens":7}}
            """));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/"), maxTokens: 4096),
            http);

        var response = await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.System, "system"), new ChatMessage(ChatRole.User, "Hi")],
            [ToolDefinition.Json("read", JsonDocument.Parse("{\"type\":\"object\"}").RootElement, "Read a file")]));

        Assert.Equal(new Uri("https://example.test/v1/messages"), handler.Request!.RequestUri);
        Assert.Equal("test-key", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
        Assert.Null(handler.Request.Headers.Authorization);
        Assert.Equal("Crystal Code", handler.Request.Headers.UserAgent.ToString());
        Assert.Contains(
            "\"system\":[{\"type\":\"text\",\"text\":\"system\",\"cache_control\":{\"type\":\"ephemeral\"}}]",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains("\"input_schema\":", handler.Body, StringComparison.Ordinal);
        Assert.Equal(FinishReason.ToolCalls, response.Candidates[0].FinishReason);
        Assert.IsType<ChatReasoningItem>(response.Candidates[0].Items[0]);
        Assert.IsType<ToolCall>(response.Candidates[0].Items[1]);
    }

    [Fact]
    public async Task StreamAsync_PreservesThinkingSignatureAndUsage()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            event: message_start
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":8,"output_tokens":1}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"plan"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"signed"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

            event: message_stop
            data: {"type":"message_stop"}

            """));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, item => item is ChatReasoningTextDelta { Text: "plan" });
        Assert.Contains(events, item => item is ChatReasoningStateReceived);
        var usage = Assert.IsType<ChatUsageReceived>(events[^2]);
        Assert.Equal(8, usage.Usage.InputTokenCount);
        Assert.Equal(5, usage.Usage.OutputTokenCount);
        Assert.IsType<ChatCandidateCompleted>(events[^1]);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysSignedThinkingWithToolResult()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"content":[
              {"type":"thinking","thinking":"plan","signature":"signed"},
              {"type":"tool_use","id":"toolu_1","name":"read","input":{}}
            ],"stop_reason":"tool_use"}
            """));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            firstHttp);
        var first = await firstProvider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        var secondHandler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var secondHttp = new HttpClient(secondHandler);
        using var secondProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            secondHttp);
        await secondProvider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            .. first.Candidates[0].Items,
            new ToolResult("toolu_1", "result")
        ]));

        Assert.Contains("\"signature\":\"signed\"", secondHandler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"tool_result\"", secondHandler.Body, StringComparison.Ordinal);
        Assert.Contains("\"tool_use_id\":\"toolu_1\"", secondHandler.Body, StringComparison.Ordinal);
        Assert.Contains(
            "\"type\":\"tool_result\",\"tool_use_id\":\"toolu_1\",\"content\":\"result\",\"is_error\":false,\"cache_control\":{\"type\":\"ephemeral\"}",
            secondHandler.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"type\":\"text\",\"text\":\"Hi\",\"cache_control\"",
            secondHandler.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_WritesExplicitDisabledThinking()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Disabled)));

        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"output_config\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_LeavesAutomaticThinkingToProviderDefault()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Automatic)));

        Assert.DoesNotContain("\"thinking\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"output_config\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_WritesAdaptiveThinkingWithSummarizedDisplay()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High)));

        Assert.Contains(
            "\"thinking\":{\"type\":\"adaptive\",\"display\":\"summarized\"}",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains("\"output_config\":{\"effort\":\"high\"}", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_OmitsThinkingDisplayWhenConfigured()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions(
                "test-key",
                "claude-test",
                new Uri("https://example.test/v1/"),
                summarizeThinking: false),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High)));

        Assert.Contains("\"thinking\":{\"type\":\"adaptive\"}", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"display\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"output_config\":{\"effort\":\"high\"}", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ForwardsMaximumAndOtherEfforts()
    {
        var maximumHandler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var maximumHttp = new HttpClient(maximumHandler);
        using var maximumProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            maximumHttp);
        await maximumProvider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.Maximum)));
        Assert.Contains("\"effort\":\"maximum\"", maximumHandler.Body, StringComparison.Ordinal);

        var maxHandler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var maxHttp = new HttpClient(maxHandler);
        using var maxProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            maxHttp);
        await maxProvider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Enabled, new ReasoningEffort("max"))));
        Assert.Contains("\"effort\":\"max\"", maxHandler.Body, StringComparison.Ordinal);

        var customHandler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var customHttp = new HttpClient(customHandler);
        using var customProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            customHttp);
        await customProvider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Enabled, new ReasoningEffort("xhigh"))));
        Assert.Contains("\"effort\":\"xhigh\"", customHandler.Body, StringComparison.Ordinal);
        Assert.Contains("\"display\":\"summarized\"", customHandler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_WritesBudgetedThinkingWithSummarizedDisplay()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(tokenBudget: 2048)));

        Assert.Contains(
            "\"thinking\":{\"type\":\"enabled\",\"budget_tokens\":2048,\"display\":\"summarized\"}",
            handler.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysForeignReasoningAsText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("thought", ReasoningTextKind.Trace)],
                new OpaqueReasoningState(
                    "openai.responses.reasoning",
                    Encoding.UTF8.GetBytes("""{"type":"reasoning","encrypted_content":"secret"}""")))),
            new ChatMessage(ChatRole.Assistant, "answer")
        ]));

        Assert.Contains("\"type\":\"text\",\"text\":\"thought\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("encrypted_content", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_OmitsForeignReasoningWithoutReadableText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            new ChatReasoningItem(new ReasoningContent(
                state: new OpaqueReasoningState(
                    "gemini.part",
                    Encoding.UTF8.GetBytes("""{"thoughtSignature":"sig"}""")))),
            new ChatMessage(ChatRole.Assistant, "answer")
        ]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        var assistant = messages[1];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        var blocks = assistant.GetProperty("content");
        var block = Assert.Single(blocks.EnumerateArray());
        Assert.Equal("text", block.GetProperty("type").GetString());
        Assert.Equal("answer", block.GetProperty("text").GetString());
        Assert.DoesNotContain("thoughtSignature", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_RejectsJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "json")],
                jsonOutput: JsonResponse.OutputSchema())));

        Assert.Contains("does not support a JSON output schema", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task StreamAsync_ThrowsJsonErrorBody()
    {
        var exception = await ReadStream(
            """{"type":"error","error":{"type":"invalid_request_error","message":"too many images"}}""");

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ended before", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ThrowsPrettyJsonErrorBody()
    {
        var exception = await ReadStream(
            """
            {
              "type": "error",
              "error": {"type": "invalid_request_error", "message": "too many images"}
            }
            """);

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ended before", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ReportsBodyWithoutEventStream()
    {
        var exception = await ReadStream(
            """{"role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""");

        Assert.Contains("did not contain an event stream", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ended before", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ReportsOfficialErrorEvent()
    {
        var exception = await ReadStreamEvents(
            """
            event: error
            data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}

            """);

        Assert.Equal("Overloaded", exception.Message);
        Assert.Equal("overloaded_error", exception.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_ReportsErrorEventWithoutMessage()
    {
        var exception = await ReadStreamEvents(
            """
            data: {"type":"error","error":{"type":"api_error"}}

            """);

        Assert.Equal("api_error", exception.ErrorCode);
        Assert.Equal("Anthropic stream failed.", exception.Message);
    }

    [Fact]
    public async Task StreamAsync_ReportsFlatErrorEvent()
    {
        var exception = await ReadStreamEvents(
            """
            data: {"type":"error","message":"flat boom","code":"server_error"}

            """);

        Assert.Equal("flat boom", exception.Message);
        Assert.Equal("server_error", exception.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_MapsErrorBodyWithOkStatus()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"type":"error","error":{"type":"invalid_request_error","message":"too many images"}}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var exception = await Assert.ThrowsAsync<AnthropicException>(
            () => provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])));

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
        Assert.Contains("response failed", exception.Message, StringComparison.Ordinal);
    }

    private static Task<AnthropicException> ReadStream(string json) =>
        Read(new RecordingHandler(JsonResponse.Create(json)));

    private static Task<AnthropicException> ReadStreamEvents(string events) =>
        Read(new RecordingHandler(JsonResponse.CreateStream(events)));

    private static async Task<AnthropicException> Read(RecordingHandler handler)
    {
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);
        return await Assert.ThrowsAsync<AnthropicException>(async () =>
        {
            await foreach (var item in provider.StreamAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])))
            {
                _ = item;
            }
        });
    }
}
