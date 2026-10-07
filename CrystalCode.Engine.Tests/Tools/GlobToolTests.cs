using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class GlobToolTests
{
    [Fact]
    public async Task InvokeAsync_ListsMatchingFiles()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "class App {}\n");
        File.WriteAllText(Path.Combine(root.Path, "README.md"), "# hi\n");
        var tool = new GlobTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GlobTool.ToolName, """{"pattern":"*.cs"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("App.cs", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_PagesMatchesWithOffsetAndLimit()
    {
        using var root = new TemporaryWorkspace();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            File.WriteAllText(Path.Combine(root.Path, name), name + "\n");
        }

        var tool = new GlobTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GlobTool.ToolName, """{"pattern":"*.txt","offset":2,"limit":2}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            "b.txt\nc.txt\n[showing 2 of 4 files; continue with offset 4]",
            output.Text);
    }

    [Fact]
    public async Task InvokeAsync_OffsetPastEnd_Fails()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "only.txt"), "x\n");
        var tool = new GlobTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GlobTool.ToolName, """{"pattern":"*.txt","offset":2}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Glob matched 1 file; offset 2 is past the end.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_FilePath_MatchReturnsRelativePath()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "class App {}\n");
        var tool = new GlobTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GlobTool.ToolName, """{"pattern":"*.cs","path":"App.cs"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("App.cs", output.Text);
    }
}
