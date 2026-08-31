using CrystalCode.Providers.OpenAI;

using Xunit;

namespace CrystalCode.Providers.Tests.OpenAI;

public sealed class OpenAICompatibleOptionsTests
{
    [Fact]
    public async Task CompleteAsync_WritesMaxTokensWhenConfigured()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "done" },
                      "finish_reason": "stop"
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIProvider(
            new OpenAIOptions(
                "test-key",
                "llama",
                new Uri("https://api.groq.com/openai/v1/"),
                maxTokens: 512,
                useMaxCompletionTokens: false,
                vendorName: "groq"),
            http);

        await provider.CompleteAsync(
            new Crystal.Chat.ChatRequest(
                [new Crystal.Chat.ChatMessage(Crystal.Chat.ChatRole.User, "Hi")]));

        Assert.Equal(
            new Uri("https://api.groq.com/openai/v1/chat/completions"),
            handler.Request?.RequestUri);
        Assert.Contains("\"max_tokens\":512", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("max_completion_tokens", handler.Body, StringComparison.Ordinal);
    }
}
