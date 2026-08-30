using Crystal.Tools;

using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

public sealed class WriteToolTests
{
    [Fact]
    public async Task InvokeAsync_CreatesNestedFile()
    {
        using var root = new TemporaryWorkspace();
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                """{"path":"src/App.cs","contents":"class App {}"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("Created src/App.cs (12 characters).", output.Text);
        Assert.Equal("class App {}", File.ReadAllText(Path.Combine(root.Path, "src", "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_RejectsPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                """{"path":"../escape.txt","contents":"no"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Path is outside the workspace.", output.Text);
    }
}
