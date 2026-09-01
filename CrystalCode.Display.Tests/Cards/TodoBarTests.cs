using CrystalCode.Display.Cards;
using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Cards;

public sealed class TodoBarTests
{
    [Fact]
    public void Lines_EmptyWhenNoItems()
    {
        Assert.Empty(TodoBar.Lines([], 80));
    }

    [Fact]
    public void Lines_ColorsMarksAndKeepsHeader()
    {
        var lines = TodoBar.Lines(
            [
                new TodoBarItem(" ", "wait"),
                new TodoBarItem("~", "now"),
                new TodoBarItem("x", "done"),
                new TodoBarItem("-", "skip")
            ],
            80);

        Assert.Equal("  Todos", lines[0].Plain);
        Assert.Contains(Theme.Chrome, lines[1].Markup, StringComparison.Ordinal);
        Assert.Contains("[ ] wait", lines[1].Plain, StringComparison.Ordinal);
        Assert.Contains(Theme.Accent, lines[2].Markup, StringComparison.Ordinal);
        Assert.Contains("[~] now", lines[2].Plain, StringComparison.Ordinal);
        Assert.Contains(Theme.Ok, lines[3].Markup, StringComparison.Ordinal);
        Assert.Contains("[x] done", lines[3].Plain, StringComparison.Ordinal);
        Assert.Contains(Theme.Muted, lines[4].Markup, StringComparison.Ordinal);
        Assert.Contains("[-] skip", lines[4].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_CapsVisibleItems()
    {
        var items = new List<TodoBarItem>();
        for (var i = 0; i < 6; i++)
        {
            items.Add(new TodoBarItem(" ", $"item {i}"));
        }

        var lines = TodoBar.Lines(items, 80);

        Assert.Equal(1 + TodoBar.MaximumVisible + 1, lines.Count);
        Assert.Contains("+2 more", lines[^1].Plain, StringComparison.Ordinal);
    }
}
