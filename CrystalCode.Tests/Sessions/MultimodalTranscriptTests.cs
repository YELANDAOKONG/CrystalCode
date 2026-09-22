using Crystal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class MultimodalTranscriptTests
{
    [Fact]
    public void Convert_ReplacesMarkersWithImagesInExactOrder()
    {
        var images = new Dictionary<int, ImageAttachment>
        {
            [1] = new(
                1,
                "image/png",
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })
        };

        var converted = MultimodalTranscript.Convert(
            [new ChatMessage(ChatRole.User, "before [Image #1] after")],
            images);

        var message = Assert.IsType<MultimodalMessage>(Assert.Single(converted));
        Assert.Equal(3, message.Contents.Count);
        Assert.Equal("before ", Assert.IsType<TextContent>(message.Contents[0]).Text);
        var image = Assert.IsType<ImageContent>(message.Contents[1]);
        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
            Assert.IsType<InlineMediaSource>(image.Image.Source).Data.ToArray());
        Assert.Equal(" after", Assert.IsType<TextContent>(message.Contents[2]).Text);
    }

    [Fact]
    public void Convert_ProjectsToolResultMarkerAsTypedImage()
    {
        var images = new Dictionary<int, ImageAttachment>
        {
            [2] = new(
                2,
                "image/webp",
                new Uri("https://example.test/result.webp"))
        };

        var converted = MultimodalTranscript.Convert(
            [new ToolResult("call_1", "rendered\n\n[Image #2]")],
            images);

        var result = Assert.IsType<MultimodalToolResult>(Assert.Single(converted));
        Assert.Equal(2, result.Contents.Count);
        var image = Assert.IsType<ImageContent>(result.Contents[1]);
        Assert.Equal(
            new Uri("https://example.test/result.webp"),
            Assert.IsType<UriMediaSource>(image.Image.Source).Uri);
    }
}
