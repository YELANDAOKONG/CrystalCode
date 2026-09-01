using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class TodoListTests
{
    [Fact]
    public void Format_UsesCheckboxMarks()
    {
        var todos = new TodoList();
        todos.Replace(
        [
            new TodoItem("1", "wait", TodoStatus.Pending),
            new TodoItem("2", "now", TodoStatus.InProgress),
            new TodoItem("3", "done", TodoStatus.Completed),
            new TodoItem("4", "skip", TodoStatus.Cancelled)
        ]);

        var text = todos.Format();

        Assert.Contains("- [ ] wait", text, StringComparison.Ordinal);
        Assert.Contains("- [~] now", text, StringComparison.Ordinal);
        Assert.Contains("- [x] done", text, StringComparison.Ordinal);
        Assert.Contains("- [-] skip", text, StringComparison.Ordinal);
    }
}
