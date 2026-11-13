using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class TodoWriteToolTests
{
    [Theory]
    [InlineData("", "a,b,c,d,e")]
    [InlineData(",\"reorder\":false", "a,b,c,d,e")]
    [InlineData(",\"reorder\":true", "d,c,b,e,a")]
    public async Task InvokeAsync_MergePreservesPositionsUnlessReorderIsTrue(
        string reorderArgument,
        string expectedOrder)
    {
        var todos = new TodoList();
        todos.Replace(
        [
            new TodoItem("a", "First", TodoStatus.Pending),
            new TodoItem("b", "Second", TodoStatus.Pending),
            new TodoItem("c", "Third", TodoStatus.Pending)
        ]);
        var tool = new TodoWriteTool(todos);

        var result = await tool.InvokeAsync(
            JsonToolCall(
                "merge",
                TodoWriteTool.ToolName,
                "{\"merge\":true" + reorderArgument + """
                ,"todos":[{"id":"d","content":"Fourth","status":"pending"},{"id":"c","content":"Third updated","status":"completed"},{"id":"b","content":"Second updated","status":"in_progress"},{"id":"e","content":"Fifth","status":"pending"}]}
                """));

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Equal(expectedOrder.Split(','), todos.Snapshot().Select(item => item.Id));
        Assert.Contains("[x] Third updated", result.Text);
        Assert.Contains("[~] Second updated", result.Text);
        Assert.Equal(todos.Format(), result.Text);
    }

    [Fact]
    public async Task InvokeAsync_ReplaceIgnoresReorderAndUsesInputOrder()
    {
        var todos = new TodoList();
        todos.Replace([new TodoItem("a", "First", TodoStatus.Pending)]);
        var tool = new TodoWriteTool(todos);

        var result = await tool.InvokeAsync(
            JsonToolCall(
                "replace",
                TodoWriteTool.ToolName,
                """{"reorder":true,"todos":[{"id":"c","content":"Third","status":"pending"},{"id":"b","content":"Second","status":"in_progress"}]}"""));

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Equal(new[] { "c", "b" }, todos.Snapshot().Select(item => item.Id));
    }

    [Fact]
    public async Task InvokeAsync_InvalidReorderLeavesListUnchanged()
    {
        var todos = new TodoList();
        var original = new TodoItem("a", "First", TodoStatus.Pending);
        todos.Replace([original]);
        var tool = new TodoWriteTool(todos);

        var result = await tool.InvokeAsync(
            JsonToolCall(
                "invalid",
                TodoWriteTool.ToolName,
                """{"merge":true,"reorder":"true","todos":[]}"""));

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Equal("reorder must be a boolean when supplied.", result.Text);
        Assert.Equal(original, Assert.Single(todos.Snapshot()));
    }

    [Fact]
    public async Task InvokeAsync_ReplacesThenMergesById()
    {
        var todos = new TodoList();
        var tool = new TodoWriteTool(todos);

        var replaced = await tool.InvokeAsync(
            JsonToolCall(
                "1",
                TodoWriteTool.ToolName,
                """{"todos":[{"id":"a","content":"first","status":"pending"}]}"""));
        var merged = await tool.InvokeAsync(
            JsonToolCall(
                "2",
                TodoWriteTool.ToolName,
                """{"merge":true,"todos":[{"id":"a","content":"first","status":"completed"},{"id":"b","content":"second","status":"in_progress"}]}"""));

        Assert.Equal(ToolResultStatus.Success, replaced.Status);
        Assert.Equal(ToolResultStatus.Success, merged.Status);
        Assert.Equal(2, todos.Count);
        Assert.Contains("[x] first", merged.Text);
        Assert.Contains("[~] second", merged.Text);
    }
}
