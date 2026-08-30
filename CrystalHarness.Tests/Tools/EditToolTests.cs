using Crystal.Tools;

using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

public sealed class EditToolTests
{
    [Fact]
    public async Task InvokeAsync_ReplacesUniqueOccurrence()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "var name = \"old\";\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("Edited App.cs.", output.Text);
        Assert.Equal("var name = \"new\";\n", File.ReadAllText(Path.Combine(root.Path, "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonUniqueMatch()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "old old\n");
        var tool = new EditTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                EditTool.ToolName,
                """{"path":"App.cs","old_string":"old","new_string":"new"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("old_string matches 2 times; it must be unique.", output.Text);
        Assert.Equal("old old\n", File.ReadAllText(Path.Combine(root.Path, "App.cs")));
    }
}
