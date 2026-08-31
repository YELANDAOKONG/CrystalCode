using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class BashToolTests
{
    [Fact]
    public async Task InvokeAsync_RunsCommandInWorkspaceRoot()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "hello");
        var tool = new BashTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"cat note.txt"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.StartsWith("exit 0", output.Text);
        Assert.Contains("hello", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ReportsNonZeroExit()
    {
        using var root = new TemporaryWorkspace();
        var tool = new BashTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"exit 7"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.StartsWith("exit 7", output.Text);
    }
}
