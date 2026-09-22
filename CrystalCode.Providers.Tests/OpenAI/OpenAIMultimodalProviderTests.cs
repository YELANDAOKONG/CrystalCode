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
}
