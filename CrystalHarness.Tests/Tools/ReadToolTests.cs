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
    public async Task InvokeAsync_ReadsPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var file = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(file, "hello\n");
        var tool = new ReadTool(new Workspace(root.Path));
        var json = "{\"path\":\"" + file.Replace("\\", "/") + "\"}";

        var output = await tool.InvokeAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("1|hello", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_RejectsCredentialPath()
    {
        using var root = new TemporaryWorkspace();
        var tool = new ReadTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ReadTool.ToolName, """{"path":"~/.ssh/id_rsa"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Reading credential paths is not allowed.", output.Text);
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
