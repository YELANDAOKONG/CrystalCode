using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Providers.Gemini;

using Xunit;

namespace CrystalCode.Providers.Tests.Gemini;

public sealed class GeminiProviderTests
{
    [Fact]
    public async Task CompleteAsync_ReplaysSignedToolCallAndResult()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"candidates":[{"content":{"parts":[
              {"functionCall":{"id":"call_1","name":"read","args":{"path":"a"}},"thoughtSignature":"signed"}
            ]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":3,"thoughtsTokenCount":2}}
            """));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new GeminiProvider(
            new GeminiOptions("test-key", "gemini-test"), firstHttp);
        using var schema = JsonDocument.Parse("{\"type\":\"object\"}");
        var request = new ChatRequest(
            [new ChatMessage(ChatRole.System, "system"), new ChatMessage(ChatRole.User, "read a")],
            [new ToolDefinition("read", schema.RootElement, "Read a file")]);

        var first = await firstProvider.CompleteAsync(request);

        Assert.Equal(new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent"),
            firstHandler.Request!.RequestUri);
        Assert.Equal("test-key", firstHandler.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Null(firstHandler.Request.Headers.Authorization);
        Assert.Equal(FinishReason.ToolCalls, first.Candidates[0].FinishReason);
        Assert.Equal(5, first.Usage!.OutputTokenCount);
        var call = Assert.IsType<ToolCall>(first.Candidates[0].Items[0]);

        var secondHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var secondHttp = new HttpClient(secondHandler);
        using var secondProvider = new GeminiProvider(
            new GeminiOptions("test-key", "gemini-test"), secondHttp);
        await secondProvider.CompleteAsync(new ChatRequest(
            [.. request.Items, .. first.Candidates[0].Items, new ToolResult(call.CallId, "contents")],
            request.Tools));

        using var sent = JsonDocument.Parse(secondHandler.Body!);
        var contents = sent.RootElement.GetProperty("contents");
        Assert.Equal("signed", contents[1].GetProperty("parts")[0]
            .GetProperty("thoughtSignature").GetString());
        Assert.Equal("call_1", contents[2].GetProperty("parts")[0]
            .GetProperty("functionResponse").GetProperty("id").GetString());
    }

    [Fact]
    public async Task StreamAsync_EmitsToolSignatureAndCompletion()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"candidates":[{"index":0,"content":{"parts":[{"functionCall":{"id":"call_1","name":"read","args":{}},"thoughtSignature":"signed"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":3,"candidatesTokenCount":1}}

            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("test-key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();

        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Contains(events, item => item is ChatToolCallDelta { CallIdDelta: "call_1" });
        Assert.Contains(events, item => item is ChatReasoningStateReceived);
        Assert.Contains(events, item => item is ChatCandidateCompleted);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysNativeCallWithoutInventingWireId()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"read","args":{}},"thoughtSignature":"signed"}]},"finishReason":"STOP"}]}"""));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), firstHttp);
        var original = new ChatRequest([new ChatMessage(ChatRole.User, "read")]);
        var response = await firstProvider.CompleteAsync(original);
        var call = Assert.IsType<ToolCall>(response.Candidates[0].Items[0]);

        var nextHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var nextHttp = new HttpClient(nextHandler);
        using var nextProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), nextHttp);
        await nextProvider.CompleteAsync(new ChatRequest(
            [.. original.Items, .. response.Candidates[0].Items, new ToolResult(call.CallId, "ok")]));

        using var sent = JsonDocument.Parse(nextHandler.Body!);
        var contents = sent.RootElement.GetProperty("contents");
        var replayCall = contents[1].GetProperty("parts")[0].GetProperty("functionCall");
        var replayResult = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.False(replayCall.TryGetProperty("id", out _));
        Assert.False(replayResult.TryGetProperty("id", out _));
        Assert.Equal("signed", contents[1].GetProperty("parts")[0]
            .GetProperty("thoughtSignature").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ReplaysSignatureOnlyPart()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"answer"},{"text":"","thoughtSignature":"signed"}]},"finishReason":"STOP"}]}"""));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), firstHttp);
        var original = new ChatRequest([new ChatMessage(ChatRole.User, "question")]);
        var response = await firstProvider.CompleteAsync(original);

        var nextHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"next"}]},"finishReason":"STOP"}]}"""));
        using var nextHttp = new HttpClient(nextHandler);
        using var nextProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), nextHttp);
        await nextProvider.CompleteAsync(new ChatRequest(
            [.. original.Items, .. response.Candidates[0].Items, new ChatMessage(ChatRole.User, "next")]));

        using var sent = JsonDocument.Parse(nextHandler.Body!);
        var parts = sent.RootElement.GetProperty("contents")[1].GetProperty("parts");
        Assert.Equal("signed", parts[1].GetProperty("thoughtSignature").GetString());
        Assert.Equal(string.Empty, parts[1].GetProperty("text").GetString());
    }
}
