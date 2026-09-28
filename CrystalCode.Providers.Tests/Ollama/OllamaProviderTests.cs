using System.Text.Json;

using Crystal;
using Crystal.Chat;
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
}
