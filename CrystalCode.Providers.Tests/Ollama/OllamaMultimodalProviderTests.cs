using System.Text.Json;

using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
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

    [Fact]
    public async Task CompleteAsync_WritesToolMessageImages()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"message":{"role":"assistant","content":"done"},"done":true}"""));
        using var http = new HttpClient(handler);
        using var provider = new OllamaMultimodalProvider(new OllamaOptions("vision-test"), http);

        await provider.CompleteAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(MultimodalChatRole.User, [new TextContent("inspect")]),
            new MultimodalToolCall("call_1", "render", "{}"),
            new MultimodalToolResult(
                "call_1",
                [
                    new TextContent("rendered"),
                    new ImageContent(new ImageMedia(
                        new InlineMediaSource(new byte[] { 4, 5, 6 }),
                        new MediaMimeType("image/png")))
                ])
        ]));

        var body = handler.Body ?? throw new InvalidOperationException("Request body was empty.");
        using var document = JsonDocument.Parse(body);
        JsonElement? tool = null;
        foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (message.GetProperty("role").GetString() == "tool")
            {
                tool = message;
            }
        }

        var found = Assert.NotNull(tool);
        Assert.Equal("rendered", found.GetProperty("content").GetString());
        Assert.Equal("BAUG", found.GetProperty("images")[0].GetString());
        Assert.DoesNotContain("crystal-image", handler.Body, StringComparison.Ordinal);
    }
}
