using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class ViewImageToolTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    [Fact]
    public void ForSession_OmitsTheToolWhenTheModelCannotSeeImages()
    {
        using var root = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);

        Assert.Empty(ViewImageTool.ForSession(workspace, sessionAcceptsImages: false));
        var tools = ViewImageTool.ForSession(workspace, sessionAcceptsImages: true);
        var tool = Assert.Single(tools);
        Assert.Equal(ViewImageTool.ToolName, tool.Definition.Name);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsTheImageBytes()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(root.Path, "shot.png"), Png);
        var tool = new ViewImageTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new MultimodalToolCall("1", ViewImageTool.ToolName, """{"path":"shot.png"}"""));

        Assert.Equal(MultimodalToolResultStatus.Success, output.Status);
        Assert.Equal("Image loaded.", Assert.IsType<TextContent>(output.Contents[0]).Text);
        var image = Assert.IsType<ImageContent>(output.Contents[1]);
        var source = Assert.IsType<InlineMediaSource>(image.Image.Source);
        Assert.True(source.Data.Span.SequenceEqual(Png));
    }

    [Fact]
    public async Task InvokeAsync_RejectsAFileThatIsNotAnImage()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "notes.txt"), "hello");
        var tool = new ViewImageTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new MultimodalToolCall("1", ViewImageTool.ToolName, """{"path":"notes.txt"}"""));

        Assert.Equal(MultimodalToolResultStatus.Failure, output.Status);
        Assert.Contains(
            "not a supported",
            Assert.IsType<TextContent>(output.Contents[0]).Text,
            StringComparison.Ordinal);
    }
}
