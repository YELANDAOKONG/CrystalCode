using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class TodoReadToolTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsTheCurrentList()
    {
        var todos = new TodoList();
        todos.Replace(
        [
            new TodoItem("1", "wait", TodoStatus.Pending),
            new TodoItem("2", "now", TodoStatus.InProgress)
        ]);
        var tool = new TodoReadTool(todos);

        var output = await tool.InvokeAsync(new ToolCall("1", TodoReadTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("- [ ] wait", output.Text, StringComparison.Ordinal);
        Assert.Contains("- [~] now", output.Text, StringComparison.Ordinal);
        Assert.Equal(2, todos.Count);
    }

    [Fact]
    public async Task InvokeAsync_ReportsEmptyList()
    {
        var tool = new TodoReadTool(new TodoList());

        var output = await tool.InvokeAsync(new ToolCall("1", TodoReadTool.ToolName, "{}"));

        Assert.Equal("No todos.", output.Text);
    }
}
