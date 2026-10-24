using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers;
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
    public async Task StreamAsync_CountsCacheTokensAsInput()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":3,"cache_creation_input_tokens":100,"cache_read_input_tokens":900,"output_tokens":1}}}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":20}}

            data: {"type":"message_stop"}

            """);

        var usage = events.OfType<ChatUsageReceived>().Last().Usage;
        Assert.Equal(1003, usage.InputTokenCount);
        Assert.Equal(20, usage.OutputTokenCount);
    }

    [Fact]
    public async Task StreamAsync_ReadsInputSideFromMessageDelta()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":2,"output_tokens":1}}}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":2,"cache_read_input_tokens":700,"cache_creation_input_tokens":null,"output_tokens":5}}

            data: {"type":"message_stop"}

            """);

        var usage = events.OfType<ChatUsageReceived>().Last().Usage;
        Assert.Equal(702, usage.InputTokenCount);
        Assert.Equal(5, usage.OutputTokenCount);
    }

    [Fact]
    public async Task StreamAsync_KeepsInputWhenMessageDeltaReportsZero()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":50,"output_tokens":1}}}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":0,"output_tokens":9}}

            data: {"type":"message_stop"}

            """);

        var usage = events.OfType<ChatUsageReceived>().Last().Usage;
        Assert.Equal(50, usage.InputTokenCount);
        Assert.Equal(9, usage.OutputTokenCount);
    }

    [Fact]
    public async Task StreamAsync_ReportsUsageAsSoonAsInputIsKnown()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":3,"cache_read_input_tokens":900,"output_tokens":1}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Working..."}}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":20}}

            data: {"type":"message_stop"}

            """);

        Assert.IsType<ChatUsageReceived>(events[0]);
        Assert.Equal(
            [new TokenUsage(903, 1), new TokenUsage(903, 20)],
            events.OfType<ChatUsageReceived>().Select(static received => received.Usage));
    }

    [Fact]
    public async Task StreamAsync_ReportsUsageBeforeStreamBreaks()
    {
        using var http = new HttpClient(new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":3,"cache_read_input_tokens":900,"output_tokens":1}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Working..."}}

            """)));
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);
        var events = new List<ChatStreamEvent>();

        await Assert.ThrowsAsync<AnthropicException>(async () =>
        {
            await foreach (var streamEvent in provider.StreamAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])))
            {
                events.Add(streamEvent);
            }
        });

        var usage = Assert.Single(events.OfType<ChatUsageReceived>());
        Assert.Equal(new TokenUsage(903, 1), usage.Usage);
    }

    [Theory]
    [InlineData("max_tokens", "length")]
    [InlineData("model_context_window_exceeded", "length")]
    [InlineData("refusal", "content_filter")]
    public async Task StreamAsync_MapsStopReasonToFinishReason(string stopReason, string expected)
    {
        var events = await CollectStreamEvents(
            $$$$"""
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":3,"output_tokens":1}}}

            data: {"type":"message_delta","delta":{"stop_reason":"{{{{stopReason}}}}"},"usage":{"output_tokens":4}}

            data: {"type":"message_stop"}

            """);

        var completed = events.OfType<ChatCandidateCompleted>().Single();
        Assert.Equal(expected, completed.FinishReason.Value);
    }

    [Theory]
    [InlineData("max_tokens", "length")]
    [InlineData("model_context_window_exceeded", "length")]
    [InlineData("refusal", "content_filter")]
    public async Task CompleteAsync_MapsStopReasonToFinishReason(string stopReason, string expected)
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            $$$$"""
            {"role":"assistant","content":[{"type":"text","text":"cut"}],"stop_reason":"{{{{stopReason}}}}","usage":{"input_tokens":3,"output_tokens":4}}
            """));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var response = await provider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        Assert.Equal(expected, response.Candidates[0].FinishReason.Value);
    }

    [Fact]
    public async Task StreamAsync_DoesNotRepeatAnUnchangedUsageSnapshot()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":10,"output_tokens":4}}}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":4}}

            data: {"type":"message_stop"}

            """);

        Assert.Single(events.OfType<ChatUsageReceived>());
    }

    [Fact]
    public async Task StreamAsync_OmitsUsageWhenProviderReportsNone()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[]}}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}

            data: {"type":"message_stop"}

            """);

        Assert.DoesNotContain(events, item => item is ChatUsageReceived);
        var completed = Assert.IsType<ChatCandidateCompleted>(Assert.Single(events));
        Assert.Equal(FinishReason.Stop, completed.FinishReason);
    }

    [Fact]
    public async Task StreamAsync_ReportsStreamWithoutMessageDelta()
    {
        var exception = await ReadStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":8,"output_tokens":1}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Working..."}}

            data: {"type":"message_stop"}

            """);

        Assert.Contains("ended without a stop reason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ReportsMessageDeltaWithNullStopReason()
    {
        var exception = await ReadStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":8,"output_tokens":1}}}

            data: {"type":"message_delta","delta":{"stop_reason":null},"usage":{"output_tokens":5}}

            data: {"type":"message_stop"}

            """);

        Assert.Contains("ended without a stop reason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_WrapsEventForUnknownBlock()
    {
        var exception = await ReadStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[]}}

            data: {"type":"content_block_delta","index":3,"delta":{"type":"text_delta","text":"x"}}

            """);

        Assert.Contains("malformed content_block_delta event", exception.Message, StringComparison.Ordinal);
        Assert.IsType<KeyNotFoundException>(exception.InnerException);
    }

    [Fact]
    public async Task StreamAsync_WrapsEventWithWrongFieldType()
    {
        var exception = await ReadStreamEvents(
            """
            data: {"type":"content_block_start","index":"zero","content_block":{"type":"text","text":""}}

            """);

        Assert.Contains("malformed content_block_start event", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_CountsCacheTokensAsInput()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn",
             "usage":{"input_tokens":4,"cache_creation_input_tokens":10,"cache_read_input_tokens":600,"output_tokens":7}}
            """));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var response = await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        Assert.NotNull(response.Usage);
        Assert.Equal(614, response.Usage.InputTokenCount);
        Assert.Equal(7, response.Usage.OutputTokenCount);
    }

    [Theory]
    [InlineData("""{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}""")]
    [InlineData("""{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn","usage":{}}""")]
    [InlineData("""{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn","usage":{"output_tokens":7}}""")]
    public async Task CompleteAsync_ReturnsNoUsageWhenCountsAreMissing(string body)
    {
        var handler = new RecordingHandler(JsonResponse.Create(body));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var response = await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        Assert.Null(response.Usage);
    }

    [Fact]
    public async Task CompleteAsync_WrapsContentBlockWithoutType()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        var exception = await Assert.ThrowsAsync<AnthropicException>(
            () => provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])));

        Assert.Contains("response was malformed", exception.Message, StringComparison.Ordinal);
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
    public async Task CompleteAsync_ReplaysAnEmptyArgumentCallAsAnEmptyObject()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "list"),
            new ToolCall("toolu_1", "list", "", ToolInputKind.Json),
            new ToolResult("toolu_1", "a.txt")
        ]));

        Assert.Contains("\"name\":\"list\",\"input\":{}", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_KeepsServerBlocksAndResumesAfterPauseTurn()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"content":[
              {"type":"text","text":"looking"},
              {"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{"query":"crystal"}},
              {"type":"web_search_tool_result","tool_use_id":"srvtoolu_1","content":[
                {"type":"web_search_result","url":"https://example.test","title":"T","encrypted_content":"enc"}]},
              {"type":"server_tool_use","id":"srvtoolu_2","name":"web_search","input":{"query":"more"}}
            ],"stop_reason":"pause_turn"}
            """));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            firstHttp);
        var first = await firstProvider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        var candidate = first.Candidates[0];
        Assert.Equal(ProviderFinishReason.PauseTurn, candidate.FinishReason);
        Assert.DoesNotContain(candidate.Items, static item => item is ToolCall);
        Assert.Equal(4, candidate.Items.Count);

        var secondHandler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var secondHttp = new HttpClient(secondHandler);
        using var secondProvider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            secondHttp);
        await secondProvider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            .. candidate.Items
        ]));

        var body = secondHandler.Body;
        Assert.Contains(
            "{\"type\":\"server_tool_use\",\"id\":\"srvtoolu_1\",\"name\":\"web_search\",\"input\":{\"query\":\"crystal\"}}",
            body,
            StringComparison.Ordinal);
        Assert.Contains("\"type\":\"web_search_tool_result\"", body, StringComparison.Ordinal);
        Assert.Contains("\"encrypted_content\":\"enc\"", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("srvtoolu_1", StringComparison.Ordinal) < body.IndexOf("srvtoolu_2", StringComparison.Ordinal));
        // The paused round ends on a verbatim block, so the cache marker moves up.
        Assert.Contains(
            "\"type\":\"text\",\"text\":\"looking\",\"cache_control\"",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_AssemblesServerBlocksWithoutSurfacingToolCalls()
    {
        var events = await CollectStreamEvents(
            """
            data: {"type":"message_start","message":{"content":[],"usage":{"input_tokens":8,"output_tokens":1}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{}}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"query\":"}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"\"crystal\"}"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"content_block_start","index":1,"content_block":{"type":"web_search_tool_result","tool_use_id":"srvtoolu_1","content":[{"type":"web_search_result","url":"https://example.test","encrypted_content":"enc"}]}}

            data: {"type":"content_block_stop","index":1}

            data: {"type":"content_block_start","index":2,"content_block":{"type":"server_tool_use","id":"srvtoolu_2","name":"web_search","input":{}}}

            data: {"type":"content_block_stop","index":2}

            data: {"type":"message_delta","delta":{"stop_reason":"pause_turn"},"usage":{"output_tokens":30}}

            data: {"type":"message_stop"}

            """);

        Assert.DoesNotContain(events, static streamEvent => streamEvent is ChatToolCallDelta);
        var states = events.OfType<ChatReasoningStateReceived>().ToArray();
        Assert.Equal(3, states.Length);
        Assert.All(states, static state => Assert.Equal("anthropic.messages.server_block", state.State.Format));
        Assert.Equal(
            "{\"type\":\"server_tool_use\",\"id\":\"srvtoolu_1\",\"name\":\"web_search\",\"input\":{\"query\":\"crystal\"}}",
            Encoding.UTF8.GetString(states[0].State.Data.Span));
        Assert.Contains("\"encrypted_content\":\"enc\"", Encoding.UTF8.GetString(states[1].State.Data.Span), StringComparison.Ordinal);
        // A call that streamed no input keeps the empty object from its start event.
        Assert.Contains("\"input\":{}", Encoding.UTF8.GetString(states[2].State.Data.Span), StringComparison.Ordinal);
        Assert.Equal(ProviderFinishReason.PauseTurn, events.OfType<ChatCandidateCompleted>().Single().FinishReason);
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

    private static async Task<List<ChatStreamEvent>> CollectStreamEvents(string events)
    {
        using var http = new HttpClient(new RecordingHandler(JsonResponse.CreateStream(events)));
        using var provider = new AnthropicProvider(
            new AnthropicOptions("test-key", "claude-test", new Uri("https://example.test/v1/")),
            http);
        var collected = new List<ChatStreamEvent>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])))
        {
            collected.Add(streamEvent);
        }

        return collected;
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
