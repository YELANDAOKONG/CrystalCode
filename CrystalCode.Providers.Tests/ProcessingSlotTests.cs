using Crystal.Chat;

using CrystalCode.Providers.Anthropic;
using CrystalCode.Providers.DeepSeek;
using CrystalCode.Providers.Gemini;
using CrystalCode.Providers.OpenAI;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Providers.Tests;

public sealed class ProcessingSlotTests
{
    [Fact]
    public async Task OpenAI_WritesEitherSlotToServiceTierAndRejectsBoth()
    {
        var tier = await SendOpenAI(serviceTier: "flex");
        Assert.Contains("\"service_tier\":\"flex\"", tier, StringComparison.Ordinal);
        Assert.DoesNotContain("\"speed\"", tier, StringComparison.Ordinal);

        var speed = await SendOpenAI(speed: "ultrafast");
        Assert.Contains("\"service_tier\":\"ultrafast\"", speed, StringComparison.Ordinal);

        var omitted = await SendOpenAI();
        Assert.DoesNotContain("service_tier", omitted, StringComparison.Ordinal);

        var handler = new RecordingHandler(JsonResponse.Create(Done()));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(
            new OpenAIOptions("test-key", "gpt-test", serviceTier: "flex", speed: "fast"),
            http);
        var rejected = await Assert.ThrowsAsync<NotSupportedException>(() => provider.CompleteAsync(Request()));
        Assert.Contains("only one of service tier and speed", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anthropic_WritesTheSlotsToSeparateFields()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}"""));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicProvider(
            new AnthropicOptions(
                "test-key",
                "claude-test",
                new Uri("https://example.test/v1/"),
                serviceTier: "standard_only",
                speed: "fast"),
            http);

        await provider.CompleteAsync(Request());

        Assert.Contains("\"service_tier\":\"standard_only\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"speed\":\"fast\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Responses_WritesEitherSlotToServiceTier()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"id":"resp","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"done"}]}],"status":"completed"}"""));
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions(
                "test-key",
                "gpt-test",
                new Uri("https://example.test/v1/"),
                speed: "fast"),
            http);

        await provider.CompleteAsync(Request());

        Assert.Contains("\"service_tier\":\"fast\"", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("flex", null)]
    [InlineData(null, "fast")]
    public async Task DeepSeek_RejectsASetSlot(string? serviceTier, string? speed)
    {
        var handler = new RecordingHandler(JsonResponse.Create(Done()));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-flash", serviceTier: serviceTier, speed: speed),
            http);

        var rejected = await Assert.ThrowsAsync<NotSupportedException>(() => provider.CompleteAsync(Request()));
        Assert.Contains("does not support service tier or speed", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gemini_RejectsASetSlot()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(
            new GeminiOptions("test-key", "gemini-test", speed: "fast"),
            http);

        var rejected = await Assert.ThrowsAsync<NotSupportedException>(() => provider.CompleteAsync(Request()));
        Assert.Contains("does not support service tier or speed", rejected.Message, StringComparison.Ordinal);
    }

    private static async Task<string> SendOpenAI(string? serviceTier = null, string? speed = null)
    {
        var handler = new RecordingHandler(JsonResponse.Create(Done()));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(
            new OpenAIOptions("test-key", "gpt-test", serviceTier: serviceTier, speed: speed),
            http);
        await provider.CompleteAsync(Request());
        return handler.Body ?? string.Empty;
    }

    private static ChatRequest Request() => new([new ChatMessage(ChatRole.User, "Hi")]);

    private static string Done() =>
        """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "done" },
              "finish_reason": "stop"
            }
          ]
        }
        """;
}
