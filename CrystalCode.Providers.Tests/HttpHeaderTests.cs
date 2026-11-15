using Crystal;
using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Anthropic;
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
            ConfiguredHeaders());
        await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "hello")]));
        AssertConfiguredHeaders(handler.Request!);
    }

    [Fact]
    public async Task ProtocolClient_SendsConfiguredHeaders()
    {
        var handler = new RecordingHandler(JsonResponse.Create("""{"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "model"), http,
            ConfiguredHeaders());
        await provider.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "hello")]));
        AssertConfiguredHeaders(handler.Request!);
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
        var headers = ConfiguredHeaders();
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

        AssertConfiguredHeaders(handler.Request!);
    }

    [Fact]
    public void ClientCreation_RejectsOnlyItsOwnProtocolHeaders()
    {
        var geminiHeaders = new Dictionary<string, string> { ["X-Goog-Api-Key"] = "redacted-value" };
        using var openAI = new OpenAIProvider(new OpenAIOptions("key", "model"), null, geminiHeaders);
        var error = Assert.Throws<InvalidOperationException>(
            () => new GeminiProvider(new GeminiOptions("key", "model"), null, geminiHeaders));
        Assert.Contains("X-Goog-Api-Key", error.Message);
        Assert.DoesNotContain("redacted-value", error.Message);

        Assert.Throws<InvalidOperationException>(() => new AnthropicMultimodalProvider(
            new AnthropicOptions("key", "model", new Uri("https://api.example/")), null,
            new Dictionary<string, string> { ["Anthropic-Version"] = "example" }));
        Assert.Throws<InvalidOperationException>(() => new OpenAIMultimodalProvider(
            new OpenAIOptions("key", "model"), null,
            new Dictionary<string, string> { ["OpenAI-Project"] = "example" }));
    }

    [Fact]
    public void ClientCreation_RejectsMalformedHostWithoutItsValue()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new OpenAIProvider(
            new OpenAIOptions("key", "model"), null,
            new Dictionary<string, string> { ["Host"] = "https://invalid.example/path" }));
        Assert.DoesNotContain("invalid.example", error.Message);
    }

    private static Dictionary<string, string> ConfiguredHeaders() => new()
    {
        ["X-Test"] = "value",
        ["user-agent"] = "GatewayClient/1.0",
        ["host"] = "gateway.example:8443"
    };

    private static void AssertConfiguredHeaders(HttpRequestMessage request)
    {
        Assert.Equal("value", Assert.Single(request.Headers.GetValues("X-Test")));
        Assert.Equal("GatewayClient/1.0", Assert.Single(request.Headers.GetValues("User-Agent")));
        Assert.Equal("gateway.example:8443", request.Headers.Host);
        Assert.NotEqual("gateway.example", request.RequestUri!.Host);
    }
}
