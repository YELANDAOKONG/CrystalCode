using System.Text;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class SearchToolTests
{
    [Fact]
    public async Task InvokeAsync_RanksNameMatchesFirst_ThenCounts_ThenPathOrder()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "Notes.txt"), "feature\n");
        Directory.CreateDirectory(Path.Combine(root.Path, "feature"));
        File.WriteAllText(Path.Combine(root.Path, "feature", "Readme.md"), "no mention\n");
        File.WriteAllText(Path.Combine(root.Path, "Zebra.cs"), "feature\nfeature\n");
        File.WriteAllText(Path.Combine(root.Path, "Apple.cs"), "feature\nfeature\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"feature"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        var nameIndex = output.Text.IndexOf("feature/Readme.md", StringComparison.Ordinal);
        var appleIndex = output.Text.IndexOf("Apple.cs", StringComparison.Ordinal);
        var zebraIndex = output.Text.IndexOf("Zebra.cs", StringComparison.Ordinal);
        var notesIndex = output.Text.IndexOf("Notes.txt", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0);
        Assert.True(appleIndex > nameIndex);
        Assert.True(zebraIndex > appleIndex);
        Assert.True(notesIndex > zebraIndex);
        Assert.Contains("(name)", output.Text);
        Assert.Contains("2 matches", output.Text);
        Assert.Contains("1 match", output.Text);
        Assert.DoesNotContain("\r", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_IsCaseInsensitiveByDefault()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "MixedCase\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"mixedcase"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_CaseSensitiveMatchesExactCaseOnly()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "MixedCase\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var miss = await tool.InvokeAsync(
            new ToolCall(
                "1",
                SearchTool.ToolName,
                """{"query":"mixedcase","caseSensitive":true}"""));

        Assert.Equal(ToolResultStatus.Success, miss.Status);
        Assert.Equal("No matches.", miss.Text);

        var hit = await tool.InvokeAsync(
            new ToolCall(
                "2",
                SearchTool.ToolName,
                """{"query":"MixedCase","caseSensitive":true}"""));

        Assert.Equal(ToolResultStatus.Success, hit.Status);
        Assert.Contains("App.cs", hit.Text);
    }

    [Fact]
    public async Task InvokeAsync_GlobNarrowsFiles()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "alpha\n");
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "alpha\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"alpha","glob":"*.cs"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs", output.Text);
        Assert.DoesNotContain("note.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_PathSelectsSingleFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "alpha\n");
        File.WriteAllText(Path.Combine(root.Path, "Other.cs"), "alpha\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"alpha","path":"App.cs"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs", output.Text);
        Assert.DoesNotContain("Other.cs", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_PathSelectsDirectory()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "sub"));
        File.WriteAllText(Path.Combine(root.Path, "sub", "Inner.cs"), "alpha\n");
        File.WriteAllText(Path.Combine(root.Path, "Other.cs"), "alpha\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"alpha","path":"sub"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("sub/Inner.cs", output.Text);
        Assert.DoesNotContain("Other.cs", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_PagesRankedFiles()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "A.cs"), "alpha\n");
        File.WriteAllText(Path.Combine(root.Path, "B.cs"), "alpha\n");
        File.WriteAllText(Path.Combine(root.Path, "C.cs"), "alpha\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var first = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"alpha","limit":1}"""));
        Assert.Equal(ToolResultStatus.Success, first.Status);
        Assert.Contains("A.cs", first.Text);
        Assert.Contains(
            "[showing 1 of 3 files; continue with offset 2]",
            first.Text,
            StringComparison.Ordinal);

        var second = await tool.InvokeAsync(
            new ToolCall("2", SearchTool.ToolName, """{"query":"alpha","offset":2,"limit":1}"""));
        Assert.Equal(ToolResultStatus.Success, second.Status);
        Assert.Contains("B.cs", second.Text);
        Assert.DoesNotContain("A.cs", second.Text);
        Assert.Contains(
            "[showing 1 of 3 files; continue with offset 3]",
            second.Text,
            StringComparison.Ordinal);

        var past = await tool.InvokeAsync(
            new ToolCall("3", SearchTool.ToolName, """{"query":"alpha","offset":9}"""));
        Assert.Equal(ToolResultStatus.Failure, past.Status);
        Assert.Contains("offset 9 is past the end.", past.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_HotFileCapsCountAndKeepsOtherFiles()
    {
        using var root = new TemporaryWorkspace();
        var hot = new StringBuilder();
        for (var index = 0; index < WorkspaceLimits.MaximumSearchMatchesPerFile + 50; index++)
        {
            hot.Append("needle\n");
        }

        File.WriteAllText(Path.Combine(root.Path, "hot.txt"), hot.ToString());
        File.WriteAllText(Path.Combine(root.Path, "other.txt"), "needle\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"needle"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("hot.txt", output.Text);
        Assert.Contains("other.txt", output.Text);
        Assert.Contains("200+ matches", output.Text);
        Assert.Contains("  1: needle", output.Text);
        Assert.Contains("  2: needle", output.Text);
        Assert.DoesNotContain("  3: needle", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ExactlyAtCountCapKeepsPlainCount()
    {
        using var root = new TemporaryWorkspace();
        var exact = new StringBuilder();
        for (var index = 0; index < WorkspaceLimits.MaximumSearchMatchesPerFile; index++)
        {
            exact.Append("needle\n");
        }

        File.WriteAllText(Path.Combine(root.Path, "exact.txt"), exact.ToString());
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"needle"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("200 matches", output.Text);
        Assert.DoesNotContain("200+", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SkipsIgnoredDirectoriesAndCredentialPaths()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "needle\n");
        Directory.CreateDirectory(Path.Combine(root.Path, "bin"));
        File.WriteAllText(Path.Combine(root.Path, "bin", "Hidden.cs"), "needle\n");
        Directory.CreateDirectory(Path.Combine(root.Path, ".ssh"));
        File.WriteAllText(Path.Combine(root.Path, ".ssh", "id_rsa"), "needle\n");
        Directory.CreateDirectory(Path.Combine(root.Path, ".crystal"));
        File.WriteAllText(
            Path.Combine(root.Path, ".crystal", "credentials.json"),
            "needle\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"needle"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("App.cs", output.Text);
        Assert.DoesNotContain("Hidden", output.Text);
        Assert.DoesNotContain("id_rsa", output.Text);
        Assert.DoesNotContain("credentials.json", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_BinaryFileMatchesByNameOnly()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllBytes(
            Path.Combine(root.Path, "data.bin"),
            [0x00, 0x6e, 0x65, 0x65, 0x64, 0x6c, 0x65]);
        var tool = new SearchTool(new Workspace(root.Path));

        var byName = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"data"}"""));

        Assert.Equal(ToolResultStatus.Success, byName.Status);
        Assert.Contains("data.bin  (name)", byName.Text, StringComparison.Ordinal);

        var byContent = await tool.InvokeAsync(
            new ToolCall("2", SearchTool.ToolName, """{"query":"needle"}"""));

        Assert.Equal(ToolResultStatus.Success, byContent.Status);
        Assert.Equal("No matches.", byContent.Text);
    }

    [Fact]
    public async Task InvokeAsync_SearchesPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(outside.Path, "note.txt"), "alpha\n");
        var tool = new SearchTool(new Workspace(root.Path));
        var json = "{\"query\":\"alpha\",\"path\":\"" + outside.Path.Replace("\\", "/") + "\"}";

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("note.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_SymlinkedFileReportsBothEntries()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "alpha\n");
        File.CreateSymbolicLink(
            Path.Combine(root.Path, "link.txt"),
            Path.Combine(root.Path, "a.txt"));
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"alpha"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("a.txt", output.Text);
        Assert.Contains("link.txt", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_TruncatesLongPreviewLines()
    {
        using var root = new TemporaryWorkspace();
        var longLine = new string('x', WorkspaceLimits.MaximumSearchPreviewCharacters + 50)
            + " needle";
        File.WriteAllText(Path.Combine(root.Path, "long.txt"), longLine + "\n");
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, """{"query":"needle"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("...", output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            new string('x', WorkspaceLimits.MaximumSearchPreviewCharacters + 50),
            output.Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"query":"("}""", "Invalid regular expression")]
    [InlineData("""{"query":""}""", "Arguments must include query")]
    [InlineData("""{"query":"x","offset":0}""", "offset and limit must be positive")]
    [InlineData("""{"query":"x","limit":-1}""", "offset and limit must be positive")]
    public async Task InvokeAsync_RejectsInvalidArguments(string json, string expected)
    {
        using var root = new TemporaryWorkspace();
        var tool = new SearchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", SearchTool.ToolName, json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains(expected, output.Text, StringComparison.Ordinal);
    }
}
