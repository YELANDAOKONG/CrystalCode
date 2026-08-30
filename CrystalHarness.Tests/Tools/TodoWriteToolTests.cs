using Crystal.Tools;

using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

public sealed class TodoWriteToolTests
{
    [Fact]
    public async Task InvokeAsync_ReplacesThenMergesById()
    {
        var todos = new TodoList();
        var tool = new TodoWriteTool(todos);

        var replaced = await tool.InvokeAsync(
            new ToolCall(
                "1",
                TodoWriteTool.ToolName,
                """{"todos":[{"id":"a","content":"first","status":"pending"}]}"""));
        var merged = await tool.InvokeAsync(
            new ToolCall(
                "2",
                TodoWriteTool.ToolName,
                """{"merge":true,"todos":[{"id":"a","content":"first","status":"completed"},{"id":"b","content":"second","status":"in_progress"}]}"""));

        Assert.Equal(ToolResultStatus.Success, replaced.Status);
        Assert.Equal(ToolResultStatus.Success, merged.Status);
        Assert.Equal(2, todos.Count);
        Assert.Contains("[completed] first", merged.Text);
        Assert.Contains("[in_progress] second", merged.Text);
    }
}
