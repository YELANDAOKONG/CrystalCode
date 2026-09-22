using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Providers.Tests.Responses;

public sealed class ResponsesMultimodalProviderTests
{
    [Fact]
    public async Task CompleteAsync_WritesInlineAndUriImages()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"done"}]}]}"""));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);
        var request = new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [
                    new TextContent("Inspect these images"),
                    Image([1, 2, 3], "image/png"),
                    Image(new Uri("https://example.test/image.webp"), "image/webp")
                ])
        ]);

        var response = await provider.CompleteAsync(request);

        Assert.Contains(
            "\"image_url\":\"data:image/png;base64,AQID\"",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"image_url\":\"https://example.test/image.webp\"",
            handler.Body,
            StringComparison.Ordinal);
        var message = Assert.IsType<MultimodalMessage>(response.Candidates[0].Items[0]);
        Assert.Equal("done", Assert.IsType<TextContent>(message.Contents[0]).Text);
    }

    [Fact]
    public async Task CompleteAsync_WritesImageToolResultAsTypedOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"done"}]}]}"""));
        using var http = new HttpClient(handler);
        using var provider = CreateProvider(http);
        var request = new MultimodalChatRequest(
        [
            new MultimodalMessage(
                MultimodalChatRole.User,
                [new TextContent("Inspect the tool output")]),
            new MultimodalToolCall("call_1", "render", "{}"),
            new MultimodalToolResult(
                "call_1",
                [new TextContent("rendered"), Image([4, 5, 6], "image/png")])
        ]);

        await provider.CompleteAsync(request);

        Assert.Contains(
            "\"type\":\"function_call_output\"",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"type\":\"input_image\"",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "data:image/png;base64,BAUG",
            handler.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_MapsTypedTextToolUsageAndCompletion()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"type":"response.output_text.delta","output_index":0,"delta":"hello"}

            data: {"type":"response.output_item.added","output_index":1,"item":{"type":"function_call","call_id":"call_1","name":"read","arguments":""}}

            data: {"type":"response.function_call_arguments.delta","output_index":1,"delta":"{}"}

            data: {"type":"response.completed","response":{"status":"completed","usage":{"input_tokens":4,"output_tokens":2}}}

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
        Assert.Contains(
            events,
            item => item is MultimodalToolCallDelta
            {
                CallIdDelta: "call_1",
                NameDelta: "read"
            });
        Assert.Contains(events, item => item is MultimodalChatUsageReceived);
        Assert.Contains(
            events,
            item => item is MultimodalChatCandidateCompleted
            {
                FinishReason: var reason
            } && reason == FinishReason.ToolCalls);
    }

    [Fact]
    public void Capabilities_AdvertiseOnlyTextAndImageInput()
    {
        using var provider = CreateProvider(new HttpClient());

        Assert.Contains(
            provider.Capabilities.Inputs,
            item => item.Modality == ContentModality.Text);
        var image = Assert.Single(
            provider.Capabilities.Inputs,
            item => item.Modality == ContentModality.Image);
        Assert.Contains(MediaSourceKind.Inline, image.SourceKinds);
        Assert.Contains(MediaSourceKind.Uri, image.SourceKinds);
        Assert.Single(provider.Capabilities.Outputs);
        Assert.Equal(ContentModality.Text, provider.Capabilities.Outputs[0].Modality);
    }

    private static ResponsesMultimodalProvider CreateProvider(HttpClient http) =>
        new(
            new ResponsesOptions(
                "test-key",
                "gpt-test",
                new Uri("https://example.test/v1/")),
            http);

    private static ImageContent Image(byte[] data, string mimeType) =>
        new(new ImageMedia(
            new InlineMediaSource(data),
            new MediaMimeType(mimeType)));

    private static ImageContent Image(Uri uri, string mimeType) =>
        new(new ImageMedia(
            new UriMediaSource(uri),
            new MediaMimeType(mimeType)));
}
