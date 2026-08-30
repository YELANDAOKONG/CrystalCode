using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalHarness.Display;

/// <summary>
/// Sticky follow-up list painted above the composer until the items are sent.
/// </summary>
public static class QueueCard
{
    public const int MaximumVisible = 4;

    public static IRenderable? TryCreate(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return null;
        }

        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        var visible = Math.Min(items.Count, MaximumVisible);
        for (var i = 0; i < visible; i++)
        {
            grid.AddRow(
                new Markup($"[{Theme.Chrome}]{i + 1}[/]"),
                new Markup($"[{Theme.User}]{MarkupText.Escape(items[i])}[/]"));
        }

        if (items.Count > MaximumVisible)
        {
            var more = items.Count - MaximumVisible;
            grid.AddRow(
                new Markup($"[{Theme.Chrome}]+[/]"),
                new Markup($"[{Theme.Chrome}]{more} more[/]"));
        }

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("Queued"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }
}
