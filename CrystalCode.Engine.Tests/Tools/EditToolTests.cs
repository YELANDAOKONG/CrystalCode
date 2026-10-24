using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class EditToolTests
{
    [Fact]
    public async Task InvokeAsync_ReplacesUniqueOccurrence()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "var name = \"old\";\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            "Edited App.cs.\nChanged lines:\n     1|var name = \"new\";",
            output.Text);
        Assert.Equal("var name = \"new\";\n", File.ReadAllText(Path.Combine(root.Path, "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_SnippetShowsFiveLinesOfContextOnEachSide()
    {
        using var root = new TemporaryWorkspace();
        var lines = Enumerable.Range(1, 20).Select(n => "line" + n).ToArray();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), string.Join('\n', lines) + "\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"a.txt","old_string":"line10\n","new_string":"ten\nten-b\n"}"""));

        var shown = output.Text.Split('\n');
        Assert.Equal("Edited a.txt.", shown[0]);
        Assert.Equal("Changed lines:", shown[1]);
        Assert.Equal("     5|line5", shown[2]);
        Assert.Equal("    10|ten", shown[7]);
        Assert.Equal("    11|ten-b", shown[8]);
        Assert.Equal("    16|line15", shown[^1]);
        Assert.Equal(2 + 12, shown.Length);
    }

    [Fact]
    public async Task InvokeAsync_SnippetClampsAtTheStartOfTheFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "head\nb\nc\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"a.txt","old_string":"head","new_string":"top"}"""));

        Assert.Equal("Edited a.txt.\nChanged lines:\n     1|top\n     2|b\n     3|c", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_DeletingTextShowsTheSurroundingLines()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "a\ndrop\nb\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"a.txt","old_string":"drop\n","new_string":""}"""));

        Assert.Equal("Edited a.txt.\nChanged lines:\n     1|a\n     2|b", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_EmptyingAFileReturnsOnlyTheSummary()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "only\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"a.txt","old_string":"only\n","new_string":""}"""));

        Assert.Equal("Edited a.txt.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ReplaceAllShowsTheFirstChange()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "x\nold\ny\nold\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"a.txt","old_string":"old","new_string":"new","replace_all":true}"""));

        Assert.StartsWith(
            "Edited a.txt, replaced 2 occurrences.\nFirst change:\n     1|x\n     2|new",
            output.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonUniqueMatch()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "old old\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("old_string matches 2 times; it must be unique.", output.Text);
        Assert.Equal("old old\n", File.ReadAllText(Path.Combine(root.Path, "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_ReplacesEveryOccurrenceWhenReplaceAllIsTrue()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "old old\nold\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new","replace_all":true}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.StartsWith("Edited App.cs, replaced 3 occurrences.", output.Text, StringComparison.Ordinal);
        Assert.Equal("new new\nnew\n", File.ReadAllText(Path.Combine(root.Path, "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_ReplaceAllStillFailsWhenTheTextIsMissing()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "keep\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new","replace_all":true}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("old_string was not found.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_RejectsANonBooleanReplaceAll()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "old old\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new","replace_all":"yes"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("replace_all must be a boolean.", output.Text);
        Assert.Equal("old old\n", File.ReadAllText(Path.Combine(root.Path, "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_EditsPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var target = Path.Combine(outside.Path, "note.md");
        File.WriteAllText(target, "old\n");
        var tool = new EditTool(new Workspace(root.Path));
        var arguments = "{\"path\":" + JsonSerializer.Serialize(target)
            + ",\"old_string\":\"old\",\"new_string\":\"new\"}";

        var output = await tool.InvokeAsync(
            JsonToolCall("1", EditTool.ToolName, arguments));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains(target, output.Text, StringComparison.Ordinal);
        Assert.Equal("new\n", File.ReadAllText(target));
    }
}
