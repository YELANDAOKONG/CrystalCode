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
            [new ToolDefinition("read", JsonDocument.Parse("{\"type\":\"object\"}").RootElement, "Read a file")]));

        Assert.Equal(new Uri("https://example.test/v1/messages"), handler.Request!.RequestUri);
        Assert.Equal("test-key", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
        Assert.Null(handler.Request.Headers.Authorization);
        Assert.Equal("Crystal Code", handler.Request.Headers.UserAgent.ToString());
        Assert.Contains("\"system\":\"system\"", handler.Body, StringComparison.Ordinal);
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
}
