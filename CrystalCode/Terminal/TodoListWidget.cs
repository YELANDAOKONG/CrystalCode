using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Terminal;

/// <summary>
/// Rounded card rendered by <c>/todos</c>.
/// </summary>
internal static class TodoListWidget
{
    public static IRenderable Create(IReadOnlyList<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        IRenderable content = items.Count == 0
            ? new Markup($"[{Theme.Muted}]No todos.[/]")
            : new Rows(items.Select(Row));
        var panel = new Panel(content)
        {
            Header = new PanelHeader($"[{Theme.Accent}]Todos[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static Markup Row(TodoItem item)
    {
        var mark = TodoList.StatusMark(item.Status);
        var color = TodoMarks.Color(mark[0]) ?? Theme.Chrome;
        return new Markup($"[{color}]{MarkupText.Escape($"[{mark}] {item.Content}")}[/]");
    }
}
