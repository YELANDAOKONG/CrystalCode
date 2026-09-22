using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using CrystalCode.Providers.Anthropic;

using Xunit;

namespace CrystalCode.Providers.Tests.Anthropic;

public sealed class AnthropicMultimodalProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesUserAndToolResultImages()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}
            """));
        using var http = new HttpClient(handler);
        using var provider = new AnthropicMultimodalProvider(
            new AnthropicOptions(
                "test-key",
                "claude-test",
                new Uri("https://example.test/v1/")),
            http);

        var response = await provider.CompleteAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [new TextContent("inspect "), InlineImage([1, 2, 3])]),
            new MultimodalToolCall("toolu_1", "render", "{}"),
            new MultimodalToolResult(
                "toolu_1",
                [
                    new TextContent("rendered"),
                    new ImageContent(new ImageMedia(
                        new UriMediaSource(new Uri("https://example.test/result.webp")),
                        new MediaMimeType("image/webp")))
                ])
        ]));

        Assert.Contains("\"type\":\"base64\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"media_type\":\"image/png\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"data\":\"AQID\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"url\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("https://example.test/result.webp", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"tool_result\"", handler.Body, StringComparison.Ordinal);
        var message = Assert.IsType<MultimodalMessage>(response.Candidates[0].Items[0]);
        Assert.Equal("done", Assert.IsType<TextContent>(message.Contents[0]).Text);
    }

    [Fact]
    public async Task CompleteAsync_RejectsAssistantImageBeforeRequest()
    {
        using var provider = new AnthropicMultimodalProvider(
            new AnthropicOptions(
                "test-key",
                "claude-test",
                new Uri("https://example.test/v1/")),
            new HttpClient());

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.CompleteAsync(
            new MultimodalChatRequest(
            [
                new MultimodalMessage(
                    MultimodalChatRole.Assistant,
                    [InlineImage([1, 2, 3])])
            ])));
    }

    private static ImageContent InlineImage(byte[] data) =>
        new(new ImageMedia(
            new InlineMediaSource(data),
            new MediaMimeType("image/png")));
}
