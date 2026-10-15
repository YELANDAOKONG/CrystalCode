using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class TreeToolTests
{
    [Fact]
    public async Task InvokeAsync_ListsDirectoriesOnlyByDefault()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "src", "inner", "deep"));
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "x");
        File.WriteAllText(Path.Combine(root.Path, "src", "app.cs"), "x");
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("src/\nsrc/inner/", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_FilesTrueListsDirectoriesThenFiles()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        File.WriteAllText(Path.Combine(root.Path, "src", "app.cs"), "x");
        File.WriteAllText(Path.Combine(root.Path, "zeta.txt"), "x");
        File.WriteAllText(Path.Combine(root.Path, "alpha.txt"), "x");
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"files":true}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("src/\nsrc/app.cs\nalpha.txt\nzeta.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ClampsDepthToTheHostMaximum()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "a", "b", "c", "d", "e"));
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"depth":99}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("a/\na/b/\na/b/c/\na/b/c/d/", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonPositiveDepth()
    {
        using var root = new TemporaryWorkspace();
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"depth":0}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("depth must be positive", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_RejectsMalformedArguments()
    {
        using var root = new TemporaryWorkspace();
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"files":"yes"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains(
            "optional path string, depth integer, and files boolean",
            output.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_SkipsIgnoredDirectories()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        Directory.CreateDirectory(Path.Combine(root.Path, "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(root.Path, "bin"));
        Directory.CreateDirectory(Path.Combine(root.Path, ".ssh"));
        File.WriteAllText(Path.Combine(root.Path, ".ssh", "id_rsa"), "SECRET");
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("src/", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SkipsCredentialFiles()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, ".crystal"));
        File.WriteAllText(
            Path.Combine(root.Path, ".crystal", "credentials.json"),
            "SECRET");
        File.WriteAllText(Path.Combine(root.Path, ".crystal", "notes.md"), "ok");
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"files":true}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(".crystal/\n.crystal/notes.md", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_CapsFilesPerDirectoryWithANote()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "big"));
        for (var index = 0;
            index < WorkspaceLimits.MaximumTreeFilesPerDirectory + 5;
            index++)
        {
            File.WriteAllText(
                Path.Combine(root.Path, "big", $"f{index:D3}.txt"),
                "x");
        }

        var tool = new TreeTool(new Workspace(root.Path));
        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"files":true}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("[+5 more files in big]", output.Text, StringComparison.Ordinal);
        Assert.Equal(
            WorkspaceLimits.MaximumTreeFilesPerDirectory + 2,
            output.Text.Split('\n').Length);
    }

    [Fact]
    public async Task InvokeAsync_TruncatesAtTheEntryCap()
    {
        using var root = new TemporaryWorkspace();
        for (var index = 0; index <= WorkspaceLimits.MaximumTreeEntries; index++)
        {
            Directory.CreateDirectory(Path.Combine(root.Path, $"d{index:D4}"));
        }

        var tool = new TreeTool(new Workspace(root.Path));
        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains(
            $"[truncated after {WorkspaceLimits.MaximumTreeEntries} entries; "
                + "narrow the path or reduce depth]",
            output.Text,
            StringComparison.Ordinal);
        Assert.Equal(WorkspaceLimits.MaximumTreeEntries + 1, output.Text.Split('\n').Length);
    }

    [Fact]
    public async Task InvokeAsync_SymlinkedDirectoryInsideWorkspaceIsWalkedOnce()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "real"));
        File.WriteAllText(Path.Combine(root.Path, "real", "note.txt"), "x");
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "link"), "real");
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"files":true}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("real/\nreal/note.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SymlinkedDirectoryOutsideWorkspaceIsSkipped()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(outside.Path, "hidden"));
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "link"), outside.Path);
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("(no directories)", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_FileArgumentReportsTheFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "x");
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"path":"note.txt"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("note.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_EmptyDirectoryReportsNoDirectories()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "empty"));
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"path":"empty"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("(no directories)", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_MissingPathFails()
    {
        using var root = new TemporaryWorkspace();
        var tool = new TreeTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, """{"path":"missing"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("Path not found", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_ListsPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(outside.Path, "data"));
        var tool = new TreeTool(new Workspace(root.Path));
        var json = "{\"path\":\"" + outside.Path.Replace("\\", "/") + "\"}";

        var output = await tool.InvokeAsync(
            new ToolCall("1", TreeTool.ToolName, json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.EndsWith("data/", output.Text, StringComparison.Ordinal);
    }
}
