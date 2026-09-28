using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Gemini;

using Xunit;

namespace CrystalCode.Providers.Tests.Gemini;

public sealed class GeminiMultimodalProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesInlineImagePart()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiMultimodalProvider(
            new GeminiOptions("test-key", "gemini-test"), http);

        var response = await provider.CompleteAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(MultimodalChatRole.User,
            [
                new TextContent("inspect"),
                new ImageContent(new ImageMedia(
                    new InlineMediaSource(new byte[] { 1, 2, 3 }),
                    new MediaMimeType("image/png")))
            ])
        ]));

        Assert.Contains("\"inlineData\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("AQID", handler.Body, StringComparison.Ordinal);
        Assert.Equal("done", Assert.IsType<TextContent>(
            Assert.IsType<MultimodalMessage>(response.Candidates[0].Items[0]).Contents[0]).Text);
    }
}
