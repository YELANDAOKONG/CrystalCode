using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.OpenAI;

using Xunit;

namespace CrystalCode.Providers.Tests.OpenAI;

public sealed class OpenAIMultimodalProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesImageUrlContentPart()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}
            """));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIMultimodalProvider(
            new OpenAIOptions("test-key", "gpt-test"),
            http);

        var response = await provider.CompleteAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [
                    new TextContent("inspect"),
                    new ImageContent(new ImageMedia(
                        new InlineMediaSource(new byte[] { 1, 2, 3 }),
                        new MediaMimeType("image/png")))
                ])
        ]));

        Assert.Contains("\"type\":\"image_url\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,AQID", handler.Body, StringComparison.Ordinal);
        var message = Assert.IsType<MultimodalMessage>(response.Candidates[0].Items[0]);
        Assert.Equal("done", Assert.IsType<TextContent>(message.Contents[0]).Text);
    }

    [Fact]
    public async Task CompleteAsync_RejectsJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIMultimodalProvider(
            new OpenAIOptions("test-key", "gpt-test"),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new MultimodalChatRequest(
                [new MultimodalMessage(MultimodalChatRole.User, [new TextContent("json")])],
                jsonOutput: JsonResponse.OutputSchema())));

        Assert.Contains("does not support a JSON output schema", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task StreamAsync_CoalescesRepeatedUsage()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"hello"},"finish_reason":null}],"usage":{"prompt_tokens":5,"completion_tokens":0,"total_tokens":5}}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2,"total_tokens":7}}

            data: [DONE]

            """));
        using var http = new HttpClient(handler);
        using var provider = new OpenAIMultimodalProvider(
            new OpenAIOptions("test-key", "gpt-test"),
            http);
        var events = new List<MultimodalChatStreamEvent>();

        await foreach (var streamEvent in provider.StreamAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(MultimodalChatRole.User, [new TextContent("Hi")])
        ])))
        {
            events.Add(streamEvent);
        }

        var usage = Assert.Single(events.OfType<MultimodalChatUsageReceived>());
        Assert.Equal(new TokenUsage(5, 2), usage.Usage);
    }
}
