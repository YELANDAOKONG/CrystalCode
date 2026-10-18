using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using CrystalCode.Providers.DeepSeek;

using Xunit;

namespace CrystalCode.Providers.Tests.DeepSeek;

public sealed class DeepSeekMultimodalProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesUserAndToolImages()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}
            """));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);
        var request = new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [
                    new TextContent("inspect "),
                    InlineImage([1, 2, 3])
                ]),
            JsonMultimodalToolCall("call_1", "render", "{}"),
            new MultimodalToolResult(
                "call_1",
                [
                    new TextContent("rendered"),
                    UriImage(new Uri("https://example.test/result.webp"))
                ])
        ]);

        var response = await provider.CompleteAsync(request);

        Assert.Contains("\"type\":\"image_url\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains(
            "data:image/png;base64,AQID",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "https://example.test/result.webp",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains("\"role\":\"tool\"", handler.Body, StringComparison.Ordinal);
        var message = Assert.IsType<MultimodalMessage>(response.Candidates[0].Items[0]);
        Assert.Equal("done", Assert.IsType<TextContent>(message.Contents[0]).Text);
    }

    [Fact]
    public async Task StreamAsync_MapsTextAndCompletion()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"hello"},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"completion_tokens":2}}

            data: [DONE]

            """));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);
        var events = new List<MultimodalChatStreamEvent>();

        await foreach (var streamEvent in provider.StreamAsync(
            new MultimodalChatRequest(
            [
                new MultimodalMessage(
                    MultimodalChatRole.User,
                    [new TextContent("Hi")])
            ])))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, item => item is MultimodalMessageStarted);
        Assert.Contains(
            events,
            item => item is MultimodalMessageTextDelta { Text: "hello" });
        Assert.Contains(events, item => item is MultimodalChatUsageReceived);
        Assert.Contains(events, item => item is MultimodalChatCandidateCompleted);
    }

    [Fact]
    public async Task CompleteAsync_RejectsAssistantImageBeforeRequest()
    {
        using var provider = CreateProvider(new HttpClient());
        var request = new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.Assistant,
                [InlineImage([1, 2, 3])])
        ]);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(request));
    }

    [Fact]
    public async Task StreamAsync_ThrowsProviderErrorChunk()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"error":{"code":"invalid_request_error","message":"too many images"}}

            """));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => CollectAsync(provider));

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "DeepSeek chat stream failed",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ThrowsJsonErrorBody()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"error":{"code":"invalid_request_error","message":"too many images"}}"""));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => CollectAsync(provider));

        Assert.Equal("invalid_request_error", exception.ErrorCode);
        Assert.Contains("too many images", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_WritesConsecutiveToolImages()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}
            """));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);
        var request = new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [new TextContent("inspect both")]),
            JsonMultimodalToolCall("call_1", "view_image", """{"path":"one.png"}"""),
            new MultimodalToolResult(
                "call_1",
                [new TextContent("Image loaded."), InlineImage([1, 2, 3])]),
            JsonMultimodalToolCall("call_2", "view_image", """{"path":"two.png"}"""),
            new MultimodalToolResult(
                "call_2",
                [new TextContent("Image loaded."), InlineImage([4, 5, 6])])
        ]);

        await provider.CompleteAsync(request);

        Assert.Contains(
            "data:image/png;base64,AQID",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "data:image/png;base64,BAUG",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            handler.Body!.Split("\"type\":\"image_url\"", StringSplitOptions.None).Length - 1);
    }

    private static async Task CollectAsync(DeepSeekMultimodalProvider provider)
    {
        await foreach (var _ in provider.StreamAsync(new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [new TextContent("Hi")])
        ])))
        {
        }
    }

    private static DeepSeekMultimodalProvider CreateProvider(HttpClient http) =>
        new(
            new DeepSeekOptions("test-key", "deepseek-flash"),
            http);

    private static ImageContent InlineImage(byte[] data) =>
        new(new ImageMedia(
            new InlineMediaSource(data),
            new MediaMimeType("image/png")));

    private static ImageContent UriImage(Uri uri) =>
        new(new ImageMedia(
            new UriMediaSource(uri),
            new MediaMimeType("image/webp")));
}
