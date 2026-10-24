using CrystalCode.Display.Paint;
using CrystalCode.Engine.Tools;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class TodoListWidgetTests
{
    [Fact]
    public void Create_WrapsItemsInAWidthBoundedCard()
    {
        var lines = WidgetPaint.Lines(
            TodoListWidget.Create(
            [
                new TodoItem("a", "first", TodoStatus.Pending),
                new TodoItem("b", "second [with markup]", TodoStatus.InProgress),
                new TodoItem("c", "third", TodoStatus.Completed)
            ]),
            60);

        Assert.Contains(lines, line => line.Plain.Contains("Todos", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("[ ] first", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("[~] second [with markup]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("[x] third", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains('╭'));
        Assert.All(lines, line => Assert.True(TextWidth.Measure(line.Plain) <= 60));
    }

    [Fact]
    public void Create_ShowsEmptyListInsideTheCard()
    {
        var lines = WidgetPaint.Lines(TodoListWidget.Create([]), 60);

        Assert.Contains(lines, line => line.Plain.Contains("No todos.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains('╭'));
    }
}
