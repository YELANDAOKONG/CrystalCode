using System.Net;
using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Ollama;

using Xunit;

namespace CrystalCode.Providers.Tests.Ollama;

public sealed class OllamaProviderTests
{
    [Fact]
    public async Task CompleteAsync_UsesNativeToolWireWithoutApiKey()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"message":{"role":"assistant","thinking":"check","content":"","tool_calls":[
              {"function":{"name":"read","arguments":{"path":"a"}}}
            ]},"done":true,"prompt_eval_count":8,"eval_count":4}
            """));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new OllamaProvider(new OllamaOptions("qwen3:8b"), firstHttp);
        var first = await firstProvider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "read a")]));

        Assert.Equal(new Uri("http://localhost:11434/api/chat"), firstHandler.Request!.RequestUri);
        Assert.Null(firstHandler.Request.Headers.Authorization);
        Assert.Equal(FinishReason.ToolCalls, first.Candidates[0].FinishReason);
        var call = Assert.IsType<ToolCall>(first.Candidates[0].Items[1]);

        var secondHandler = new RecordingHandler(JsonResponse.Create(
            """{"message":{"role":"assistant","content":"done"},"done":true}"""));
        using var secondHttp = new HttpClient(secondHandler);
        using var secondProvider = new OllamaProvider(new OllamaOptions("qwen3:8b"), secondHttp);
        await secondProvider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "read a"),
                .. first.Candidates[0].Items,
                new ToolResult(call.CallId, "contents")]));

        using var sent = JsonDocument.Parse(secondHandler.Body!);
        var messages = sent.RootElement.GetProperty("messages");
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("read", messages[1].GetProperty("tool_calls")[0]
            .GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("read", messages[2].GetProperty("tool_name").GetString());
    }

    [Fact]
    public async Task StreamAsync_ParsesJsonLinesAndUsage()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"message":{"role":"assistant","thinking":"plan"},"done":false}
            {"message":{"role":"assistant","content":"hello"},"done":false}
            {"done":true,"prompt_eval_count":5,"eval_count":2}
            """));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);
        var events = new List<ChatStreamEvent>();

        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Contains(events, item => item is ChatReasoningTextDelta { Text: "plan" });
        Assert.Contains(events, item => item is ChatTextDelta { Text: "hello" });
        Assert.Contains(events, item => item is ChatUsageReceived);
        Assert.Contains(events, item => item is ChatCandidateCompleted);
    }

    [Fact]
    public async Task CompleteAsync_MapsLengthAndDropsToolCalls()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"message":{"role":"assistant","content":"partial","tool_calls":[
              {"function":{"name":"read","arguments":{}}}
            ]},"done":true,"done_reason":"length"}
            """));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);

        var response = await provider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "read")]));

        Assert.Equal(FinishReason.Length, response.Candidates[0].FinishReason);
        Assert.Equal("partial", Assert.IsType<ChatMessage>(Assert.Single(response.Candidates[0].Items)).Text);
    }

    [Fact]
    public async Task CompleteAsync_ConcatenatesOneReasoningBlock()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"message":{"role":"assistant","content":"done"},"done":true}"""));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatReasoningItem(new ReasoningContent(
            [
                new ReasoningText("one", ReasoningTextKind.Trace),
                new ReasoningText("two", ReasoningTextKind.Summary)
            ]))
        ]));

        Assert.Contains("\"thinking\":\"onetwo\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_MergesMultipleReadableReasoningBlocks()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"message":{"role":"assistant","content":"done"},"done":true}"""));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("one", ReasoningTextKind.Trace)])),
            new ChatReasoningItem(new ReasoningContent(
                state: new OpaqueReasoningState(
                    "gemini.part",
                    Encoding.UTF8.GetBytes("""{"text":"two","thoughtSignature":"sig"}""")))),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("two", ReasoningTextKind.Trace)]))
        ]));

        Assert.Contains("\"thinking\":\"onetwo\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("thoughtSignature", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_KeepsReasoningWithToolCallAndDropsStateOnlyBlock()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"message":{"role":"assistant","content":"done"},"done":true}"""));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("thought", ReasoningTextKind.Trace)])),
            new ToolCall("call_1", "read", "{}"),
            new ChatReasoningItem(new ReasoningContent(
                state: new OpaqueReasoningState(
                    "gemini.part",
                    Encoding.UTF8.GetBytes(
                        """{"functionCall":{"name":"read","args":{}},"thoughtSignature":"sig"}""")))),
            new ToolResult("call_1", "result")
        ]));

        Assert.Contains("\"thinking\":\"thought\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"tool_calls\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"tool_name\":\"read\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("thoughtSignature", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_MapsLengthDoneReason()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"message":{"role":"assistant","content":"hi"},"done":false}
            {"done":true,"done_reason":"length"}
            """));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Contains(
            events,
            item => item is ChatCandidateCompleted { FinishReason: var reason }
                && reason == FinishReason.Length);
    }

    [Fact]
    public async Task StreamAsync_EmitsToolCallsWhenTheFinishIsStop()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"message":{"role":"assistant","tool_calls":[{"function":{"name":"read","arguments":{"path":"a"}}}]},"done":false}
            {"done":true,"done_reason":"stop"}
            """));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);
        var events = new List<ChatStreamEvent>();

        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "read a")])))
        {
            events.Add(item);
        }

        Assert.Contains(events, item => item is ChatToolCallDelta { NameDelta: "read" });
        Assert.Contains(
            events,
            item => item is ChatCandidateCompleted { FinishReason: var reason }
                && reason == FinishReason.ToolCalls);
    }

    [Fact]
    public async Task StreamAsync_DropsToolCallsWhenTheFinishIsLength()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"message":{"role":"assistant","content":"partial","tool_calls":[{"function":{"name":"read","arguments":{}}}]},"done":false}
            {"done":true,"done_reason":"length"}
            """));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);
        var events = new List<ChatStreamEvent>();

        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "read")])))
        {
            events.Add(item);
        }

        Assert.DoesNotContain(events, item => item is ChatToolCallDelta);
        Assert.Contains(events, item => item is ChatTextDelta { Text: "partial" });
        Assert.Contains(
            events,
            item => item is ChatCandidateCompleted { FinishReason: var reason }
                && reason == FinishReason.Length);
    }

    [Fact]
    public async Task CompleteAsync_MapsAStringErrorBody()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"error":"model 'qwen' not found"}""",
            HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);

        var exception = await Assert.ThrowsAsync<OllamaException>(
            () => provider.CompleteAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "hi")])));

        Assert.Equal(404, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
        Assert.Contains("model 'qwen' not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_RejectsUnmatchedToolCallAndJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new OllamaProvider(new OllamaOptions("qwen3:8b"), http);

        var missing = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "read"),
                new ToolCall("call_1", "read", "{}")
            ])));
        Assert.Contains("missing a tool result for 'read'", missing.Message, StringComparison.Ordinal);

        var json = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "json")],
                jsonOutput: JsonResponse.OutputSchema())));
        Assert.Contains("does not support a JSON output schema", json.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }
}
