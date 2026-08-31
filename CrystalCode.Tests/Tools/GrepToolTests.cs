using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class GrepToolTests
{
    [Fact]
    public async Task InvokeAsync_FindsLineAndSkipsIgnoredDirectory()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "class App {}\n");
        Directory.CreateDirectory(Path.Combine(root.Path, "bin"));
        File.WriteAllText(Path.Combine(root.Path, "bin", "App.cs"), "class Hidden {}\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GrepTool.ToolName, """{"pattern":"class"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs:1:class App {}", output.Text);
        Assert.DoesNotContain("Hidden", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SearchesPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(outside.Path, "note.txt"), "alpha\n");
        var tool = new GrepTool(new Workspace(root.Path));
        var json = "{\"pattern\":\"alpha\",\"path\":\"" + outside.Path.Replace("\\", "/") + "\"}";

        var output = await tool.InvokeAsync(
            new ToolCall("1", GrepTool.ToolName, json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("alpha", output.Text);
    }
}
