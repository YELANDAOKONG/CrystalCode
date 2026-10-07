using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class ListToolTests
{
    [Fact]
    public async Task InvokeAsync_ListsSortedEntriesAndMarksDirectories()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        File.WriteAllText(Path.Combine(root.Path, "src", "App.cs"), "class App {}\n");
        File.WriteAllText(Path.Combine(root.Path, "Alpha.txt"), "a\n");
        File.WriteAllText(Path.Combine(root.Path, "Beta.txt"), "b\n");
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(new ToolCall("1", ListTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("Alpha.txt\nBeta.txt\nsrc/", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ListsSubdirectoryEntriesRelativeToWorkspace()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        File.WriteAllText(Path.Combine(root.Path, "src", "App.cs"), "class App {}\n");
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"path":"src"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("src/App.cs", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SkipsIgnoredDirectories()
    {
        using var root = new TemporaryWorkspace();
        foreach (var name in new[] { ".git", ".vs", "bin", "obj", "node_modules", "dist" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, name));
        }

        File.WriteAllText(Path.Combine(root.Path, "keep.txt"), "keep\n");
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(new ToolCall("1", ListTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("keep.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_PagesEntriesWithOffsetAndLimit()
    {
        using var root = new TemporaryWorkspace();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            File.WriteAllText(Path.Combine(root.Path, name), name + "\n");
        }

        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"offset":2,"limit":2}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            "b.txt\nc.txt\n[showing 2 of 4 entries; continue with offset 4]",
            output.Text);
    }

    [Fact]
    public async Task InvokeAsync_OffsetPastEnd_Fails()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "only.txt"), "x\n");
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"offset":2}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Directory has 1 entries; offset 2 is past the end.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_EmptyDirectory_ReportsEmpty()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "empty"));
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"path":"empty"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("(empty directory)", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_FilePath_ListsSingleEntry()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "x\n");
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"path":"note.txt"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("note.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SkipsCredentialEntries()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, ".ssh"));
        File.WriteAllText(Path.Combine(root.Path, "visible.txt"), "v\n");
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(new ToolCall("1", ListTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("visible.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_CredentialPath_Fails()
    {
        using var root = new TemporaryWorkspace();
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"path":"~/.ssh"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Listing credential paths is not allowed.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_NonPositiveOffsetOrLimit_Fails()
    {
        using var root = new TemporaryWorkspace();
        var tool = new ListTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", ListTool.ToolName, """{"offset":0}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
    }
}
