using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Ollama;

using Xunit;

namespace CrystalCode.Providers.Tests.Ollama;

public sealed class OllamaMultimodalProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesNativeImagesArray()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"message":{"role":"assistant","content":"done"},"done":true}"""));
        using var http = new HttpClient(handler);
        using var provider = new OllamaMultimodalProvider(new OllamaOptions("vision-test"), http);

        await provider.CompleteAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(MultimodalChatRole.User,
            [
                new TextContent("inspect"),
                new ImageContent(new ImageMedia(
                    new InlineMediaSource(new byte[] { 1, 2, 3 }),
                    new MediaMimeType("image/png")))
            ])
        ]));

        Assert.Contains("\"images\":[\"AQID\"]", handler.Body, StringComparison.Ordinal);
        Assert.Null(handler.Request!.Headers.Authorization);
    }
}
