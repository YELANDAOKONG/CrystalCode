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
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"class"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs:1:class App {}", output.Text);
        Assert.DoesNotContain("Hidden", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_BlankPathAndGlob_SearchesWorkspace()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "class App {}\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                GrepTool.ToolName,
                """{"pattern":"class","path":"","glob":""}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs:1:class App {}", output.Text);
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
            JsonToolCall("1", GrepTool.ToolName, json));

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
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"SECRET|visible"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("visible", output.Text);
        Assert.DoesNotContain("SECRET", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ContextPrintsNeighborLinesAndSeparatesGroups()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(
            Path.Combine(root.Path, "a.txt"),
            "one\nhit\nthree\nfour\nfive\nsix\nhit\neight\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"hit","context":1}"""));

        Assert.Equal(
            "a.txt-1-one\na.txt:2:hit\na.txt-3-three\n--\na.txt-6-six\na.txt:7:hit\na.txt-8-eight",
            output.Text);
    }

    [Fact]
    public async Task InvokeAsync_OverlappingContextPrintsEachLineOnce()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "hit\nhit\nend\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"hit","context":1}"""));

        Assert.Equal("a.txt:1:hit\na.txt:2:hit\na.txt-3-end", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_FilesModeListsEachMatchingPathOnce()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "hit\nhit\n");
        File.WriteAllText(Path.Combine(root.Path, "b.txt"), "miss\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"hit","output_mode":"files"}"""));

        Assert.Equal("a.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_CountModeReportsMatchingLinesPerFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "hit\nhit\nmiss\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"hit","output_mode":"count"}"""));

        Assert.Equal("a.txt:2", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_LimitStopsContentModeAndSaysSo()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "hit\nhit\nhit\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"hit","limit":2}"""));

        Assert.Equal("a.txt:1:hit\na.txt:2:hit\n[truncated to 2 matches]", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_LimitStopsFilesMode()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "hit\n");
        File.WriteAllText(Path.Combine(root.Path, "b.txt"), "hit\n");
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                GrepTool.ToolName,
                """{"pattern":"hit","output_mode":"files","limit":1}"""));

        Assert.Equal(2, output.Text.Split('\n').Length);
        Assert.EndsWith("[truncated to 1 files]", output.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"pattern":"x","output_mode":"lines"}""", "output_mode must be content, files, or count.")]
    [InlineData("""{"pattern":"x","context":-1}""", "context must be between 0 and 50.")]
    [InlineData("""{"pattern":"x","context":51}""", "context must be between 0 and 50.")]
    [InlineData("""{"pattern":"x","limit":0}""", "limit must be between 1 and 10000.")]
    [InlineData("""{"pattern":"x","limit":10001}""", "limit must be between 1 and 10000.")]
    public async Task InvokeAsync_RejectsOutOfRangeOptions(string json, string expected)
    {
        using var root = new TemporaryWorkspace();
        var tool = new GrepTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(JsonToolCall("1", GrepTool.ToolName, json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal(expected, output.Text);
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
            JsonToolCall("1", GrepTool.ToolName, """{"pattern":"alpha"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("a.txt:1:alpha", output.Text);
        Assert.Contains("link.txt:1:alpha", output.Text);
        Assert.DoesNotContain("\r", output.Text);
    }
}
