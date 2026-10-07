using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

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

    [Fact]
    public async Task InvokeAsync_SkipsCredentialFilesInsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "visible\n");
        Directory.CreateDirectory(Path.Combine(root.Path, ".crystal"));
        File.WriteAllText(
            Path.Combine(root.Path, ".crystal", "credentials.json"),
            "SECRET-CRED\n");
        Directory.CreateDirectory(Path.Combine(root.Path, ".ssh"));
        File.WriteAllText(Path.Combine(root.Path, ".ssh", "id_rsa"), "SECRET-KEY\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GrepTool.ToolName, """{"pattern":"SECRET|visible"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("visible", output.Text);
        Assert.DoesNotContain("SECRET", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SymlinkedFile_SearchesBothEntries()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "alpha\n");
        File.CreateSymbolicLink(
            Path.Combine(root.Path, "link.txt"),
            Path.Combine(root.Path, "a.txt"));
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GrepTool.ToolName, """{"pattern":"alpha"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("a.txt:1:alpha", output.Text);
        Assert.Contains("link.txt:1:alpha", output.Text);
        Assert.DoesNotContain("\r", output.Text);
    }
}
