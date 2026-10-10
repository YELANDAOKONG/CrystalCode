using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Providers.Tests.Responses;

public sealed class ResponsesProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesResponsesContractAndReadsTools()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"status":"completed","output":[
              {"type":"reasoning","id":"rs_1","summary":[{"type":"summary_text","text":"plan"}],"encrypted_content":"secret"},
              {"type":"function_call","call_id":"call_1","name":"read","arguments":"{\"path\":\"a\"}"}
            ],"usage":{"input_tokens":10,"output_tokens":5,"output_tokens_details":{"reasoning_tokens":3}}}
            """));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/"), maxTokens: 2048),
            http);

        var response = await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            [new ToolDefinition("read", JsonDocument.Parse("{\"type\":\"object\"}").RootElement, null)]));

        Assert.Equal(new Uri("https://example.test/v1/responses"), handler.Request!.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("Crystal Code", handler.Request.Headers.UserAgent.ToString());
        Assert.Contains("\"store\":false", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"max_output_tokens\":2048", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"include\":[\"reasoning.encrypted_content\"]", handler.Body, StringComparison.Ordinal);
        Assert.Equal(FinishReason.ToolCalls, response.Candidates[0].FinishReason);
        Assert.IsType<ChatReasoningItem>(response.Candidates[0].Items[0]);
        Assert.IsType<ToolCall>(response.Candidates[0].Items[1]);
        Assert.Equal(3, response.Usage!.ReasoningTokenCount);
    }

    [Fact]
    public async Task StreamAsync_MapsTextToolUsageAndCompletion()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.output_text.delta","output_index":0,"delta":"hello"}

            data: {"type":"response.output_item.added","output_index":1,"item":{"type":"function_call","call_id":"call_1","name":"read","arguments":""}}

            data: {"type":"response.function_call_arguments.delta","output_index":1,"delta":"{}"}

            data: {"type":"response.completed","response":{"status":"completed","usage":{"input_tokens":4,"output_tokens":2}}}

            """));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, item => item is ChatTextDelta { Text: "hello" });
        Assert.Contains(events, item => item is ChatToolCallDelta { CallIdDelta: "call_1", NameDelta: "read" });
        Assert.Contains(events, item => item is ChatUsageReceived);
        Assert.Contains(events, item => item is ChatCandidateCompleted { FinishReason: var reason } && reason == FinishReason.ToolCalls);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysOpaqueReasoningBeforeToolOutput()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"status":"completed","output":[
              {"type":"reasoning","id":"rs_1","summary":[],"encrypted_content":"encrypted"},
              {"type":"function_call","call_id":"call_1","name":"read","arguments":"{}"}
            ]}
            """));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            firstHttp);
        var first = await firstProvider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        var secondHandler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"done"}]}]}"""));
        using var secondHttp = new HttpClient(secondHandler);
        using var secondProvider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            secondHttp);
        await secondProvider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            .. first.Candidates[0].Items,
            new ToolResult("call_1", "result")
        ]));

        Assert.Contains("\"encrypted_content\":\"encrypted\"", secondHandler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"function_call_output\"", secondHandler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysForeignReasoningAsText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"done"}]}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("thought", ReasoningTextKind.Trace)],
                new OpaqueReasoningState(
                    "anthropic.messages.thinking",
                    Encoding.UTF8.GetBytes(
                        """{"type":"thinking","thinking":"thought","signature":"sig"}""")))),
            new ChatMessage(ChatRole.Assistant, "answer")
        ]));

        Assert.Contains(
            "\"type\":\"message\",\"role\":\"assistant\",\"content\":\"thought\"",
            handler.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("signature", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_OmitsForeignReasoningWithoutReadableText()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"done"}]}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "Hi"),
            new ChatReasoningItem(new ReasoningContent(
                state: new OpaqueReasoningState(
                    "deepseek.reasoning_content",
                    Encoding.UTF8.GetBytes("thought")))),
            new ChatMessage(ChatRole.Assistant, "answer")
        ]));

        Assert.DoesNotContain("\"type\":\"reasoning\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"answer\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_DisablesReasoningWithNoneEffort()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"done"}]}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "Hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Disabled)));

        Assert.Contains("\"reasoning\":{\"effort\":\"none\"}", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_KeepsRefusalTextAsContentFilter()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"status":"completed","output":[
              {"type":"message","content":[{"type":"refusal","refusal":"no"}]}
            ]}
            """));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        var response = await provider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        Assert.Equal(FinishReason.ContentFilter, response.Candidates[0].FinishReason);
        Assert.Equal("no", Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]).Text);
    }

    [Fact]
    public async Task CompleteAsync_MapsIncompleteReasons()
    {
        var filtered = await ReadIncomplete(
            """
            {"status":"incomplete","incomplete_details":{"reason":"content_filter"},"output":[{"type":"message","content":[{"type":"output_text","text":"x"}]}]}
            """);
        var truncated = await ReadIncomplete(
            """
            {"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[{"type":"message","content":[{"type":"output_text","text":"x"}]}]}
            """);
        var unspecified = await ReadIncomplete(
            """
            {"status":"incomplete","output":[{"type":"message","content":[{"type":"output_text","text":"x"}]}]}
            """);

        Assert.Equal(FinishReason.ContentFilter, filtered);
        Assert.Equal(FinishReason.Length, truncated);
        Assert.Equal("incomplete", unspecified.Value);
    }

    [Fact]
    public async Task CompleteAsync_ThrowsWhenGenerationFailed()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"failed","error":{"message":"boom","code":"server_error"}}"""));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        var exception = await Assert.ThrowsAsync<ResponsesException>(
            () => provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])));

        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_ReadsReasoningContentAsTrace()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"status":"completed","output":[
              {"type":"reasoning","id":"rs_1","summary":[{"text":"sum"}],"content":[{"text":"raw"}]}
            ]}
            """));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        var response = await provider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));

        var reasoning = Assert.IsType<ChatReasoningItem>(response.Candidates[0].Items[0]);
        Assert.Equal(ReasoningTextKind.Summary, reasoning.Content.TextSegments[0].Kind);
        Assert.Equal("sum", reasoning.Content.TextSegments[0].Text);
        Assert.Equal(ReasoningTextKind.Trace, reasoning.Content.TextSegments[1].Kind);
        Assert.Equal("raw", reasoning.Content.TextSegments[1].Text);
    }

    [Fact]
    public async Task StreamAsync_SeparatesSummaryIndexesFromReasoningText()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.reasoning_summary_text.delta","output_index":0,"summary_index":0,"delta":"one"}

            data: {"type":"response.reasoning_summary_text.delta","output_index":0,"summary_index":1,"delta":"two"}

            data: {"type":"response.reasoning_text.delta","output_index":0,"content_index":0,"delta":"trace"}

            data: {"type":"response.completed","response":{"status":"completed"}}

            """));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);
        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")])))
        {
            events.Add(streamEvent);
        }

        var deltas = events.OfType<ChatReasoningTextDelta>().ToArray();
        Assert.Equal(3, deltas.Length);
        Assert.Equal((0, ReasoningTextKind.Summary, "one"), (deltas[0].TextSegmentIndex, deltas[0].Kind, deltas[0].Text));
        Assert.Equal((1, ReasoningTextKind.Summary, "two"), (deltas[1].TextSegmentIndex, deltas[1].Kind, deltas[1].Text));
        Assert.Equal((1_000_000, ReasoningTextKind.Trace, "trace"), (deltas[2].TextSegmentIndex, deltas[2].Kind, deltas[2].Text));
    }

    [Fact]
    public async Task CompleteAsync_RejectsJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "json")],
                jsonOutput: JsonResponse.OutputSchema())));

        Assert.Contains("does not support a JSON output schema", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }

    private static async Task<FinishReason> ReadIncomplete(string json)
    {
        var handler = new RecordingHandler(JsonResponse.Create(json));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("test-key", "gpt-test", new Uri("https://example.test/v1/")),
            http);
        var response = await provider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]));
        return response.Candidates[0].FinishReason;
    }
}
