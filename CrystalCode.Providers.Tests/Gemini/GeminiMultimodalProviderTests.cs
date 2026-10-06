using System.Text.Json;

using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
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

    [Fact]
    public async Task CompleteAsync_WritesFunctionResponseImageParts()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiMultimodalProvider(
            new GeminiOptions("test-key", "gemini-test"), http);

        await provider.CompleteAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(MultimodalChatRole.User, [new TextContent("inspect")]),
            new MultimodalToolCall("call_1", "render", "{}"),
            new MultimodalToolResult(
                "call_1",
                [
                    new TextContent("rendered"),
                    new ImageContent(new ImageMedia(
                        new InlineMediaSource(new byte[] { 1, 2, 3 }),
                        new MediaMimeType("image/png")))
                ])
        ]));

        var body = handler.Body ?? throw new InvalidOperationException("Request body was empty.");
        using var document = JsonDocument.Parse(body);
        JsonElement? functionResponse = null;
        foreach (var content in document.RootElement.GetProperty("contents").EnumerateArray())
        {
            foreach (var part in content.GetProperty("parts").EnumerateArray())
            {
                if (part.TryGetProperty("functionResponse", out var response))
                {
                    functionResponse = response;
                }
            }
        }

        var found = Assert.NotNull(functionResponse);
        Assert.Equal("rendered", found.GetProperty("response").GetProperty("result").GetString());
        var inline = found.GetProperty("parts")[0].GetProperty("inlineData");
        Assert.Equal("image/png", inline.GetProperty("mimeType").GetString());
        Assert.Equal("AQID", inline.GetProperty("data").GetString());
        Assert.DoesNotContain("crystal-image", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_RejectsJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new GeminiMultimodalProvider(
            new GeminiOptions("test-key", "gemini-test"),
            http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new MultimodalChatRequest(
                [new MultimodalMessage(MultimodalChatRole.User, [new TextContent("json")])],
                jsonOutput: JsonResponse.OutputSchema())));

        Assert.Contains("does not support a JSON output schema", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }
}
