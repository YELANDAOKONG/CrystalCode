using Crystal.Tools;

using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

public sealed class ReadToolTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsNumberedLines()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "alpha\nbeta\n");
        var tool = new ReadTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ReadTool.ToolName, """{"path":"note.txt"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("1|alpha", output.Text);
        Assert.Contains("2|beta", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_RejectsPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        var tool = new ReadTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ReadTool.ToolName, """{"path":"../secret.txt"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Path is outside the workspace.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_RejectsBinaryFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(root.Path, "blob.bin"), [1, 0, 2]);
        var tool = new ReadTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ReadTool.ToolName, """{"path":"blob.bin"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("File looks binary and will not be read.", output.Text);
    }
}
