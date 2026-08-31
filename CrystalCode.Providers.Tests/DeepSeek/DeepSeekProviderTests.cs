using System.Net;
using System.Text.Json;

using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Providers.DeepSeek;

using Xunit;

namespace CrystalCode.Providers.Tests.DeepSeek;

public sealed class DeepSeekProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesChatCompletionsBody()
    {
        using var schema = JsonDocument.Parse(
            """
            {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
            """);
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "ok" },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": { "prompt_tokens": 3, "completion_tokens": 1 }
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash", maxTokens: 128),
            http);

        var response = await provider.CompleteAsync(
            new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, "Be brief."),
                    new ChatMessage(ChatRole.User, "Hello.")
                ],
                [new ToolDefinition("read", schema.RootElement, "Read a file.")],
                new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High)));

        Assert.NotNull(handler.Request);
        Assert.Equal(
            new Uri("https://api.deepseek.com/chat/completions"),
            handler.Request.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("Crystal Code", handler.Request.Headers.GetValues("User-Agent").Single());
        Assert.Contains("\"model\":\"deepseek-v4-flash\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":128", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"thinking\":{\"type\":\"enabled\"}", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"reasoning_effort\":\"high\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"read\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal("ok", Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]).Text);
        Assert.Equal(3, response.Usage?.InputTokenCount);
        Assert.Equal(1, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task CompleteAsync_OrphanedToolCall_FlushesSyntheticToolMessage()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {
                  "choices": [
                    {
                      "message": { "role": "assistant", "content": "recovered" },
                      "finish_reason": "stop"
                    }
                  ]
                }
                """));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        // Transcript has tool call with no following tool result before next user message
        var response = await provider.CompleteAsync(
            new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, "system"),
                    new ToolCall("call_dangling", "bash", "{\"command\":\"ls\"}"),
                    new ChatMessage(ChatRole.User, "next user message")
                ]));

        Assert.NotNull(handler.Body);
        Assert.Contains("\"role\":\"tool\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"tool_call_id\":\"call_dangling\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal("recovered", Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]).Text);
    }

    [Fact]
    public async Task CompleteAsync_MapsHttpErrorToDeepSeekException()
    {
        var handler = new RecordingHandler(
            JsonResponse.Create(
                """
                {"error":{"code":"invalid_request","message":"bad model"}}
                """,
                HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);
        using var provider = new DeepSeekProvider(
            new DeepSeekOptions("test-key", "deepseek-v4-flash"),
            http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => provider.CompleteAsync(
                new ChatRequest([new ChatMessage(ChatRole.User, "Hello.")])));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("bad model", exception.Message, StringComparison.Ordinal);
    }
}
