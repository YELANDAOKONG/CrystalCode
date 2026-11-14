using Crystal;
using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Gemini;
using CrystalCode.Providers.OpenAI;

using Xunit;

namespace CrystalCode.Providers.Tests;

public sealed class HttpHeaderTests
{
    [Fact]
    public async Task CompatibleClient_SendsConfiguredHeaders()
    {
        var handler = new RecordingHandler(JsonResponse.Create("""{"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(new OpenAIOptions("key", "model"), http,
            new Dictionary<string, string> { ["X-Test"] = "value" });
        await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "hello")]));
        Assert.Equal("value", handler.Request!.Headers.GetValues("X-Test").Single());
    }

    [Fact]
    public async Task ProtocolClient_SendsConfiguredHeaders()
    {
        var handler = new RecordingHandler(JsonResponse.Create("""{"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "model"), http,
            new Dictionary<string, string> { ["X-Test"] = "value" });
        await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "hello")]));
        Assert.Equal("value", handler.Request!.Headers.GetValues("X-Test").Single());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultimodalClients_SendConfiguredHeaders(bool compatible)
    {
        var response = compatible
            ? """{"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}"""
            : """{"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""";
        var handler = new RecordingHandler(JsonResponse.Create(response));
        using var http = new HttpClient(handler);
        var headers = new Dictionary<string, string> { ["X-Test"] = "value" };
        var request = new MultimodalChatRequest(
            [new MultimodalMessage(MultimodalChatRole.User, [new TextContent("hello")])]);
        if (compatible)
        {
            using var provider = new OpenAIMultimodalProvider(new OpenAIOptions("key", "model"), http, headers);
            await provider.CompleteAsync(request);
        }
        else
        {
            using var provider = new GeminiMultimodalProvider(new GeminiOptions("key", "model"), http, headers);
            await provider.CompleteAsync(request);
        }

        Assert.Equal("value", handler.Request!.Headers.GetValues("X-Test").Single());
    }

}
