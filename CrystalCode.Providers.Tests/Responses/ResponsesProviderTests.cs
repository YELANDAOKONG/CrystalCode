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
}
