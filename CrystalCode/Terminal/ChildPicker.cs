using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Home;

namespace CrystalCode.Terminal;

internal sealed class ChildPicker(
    SessionRenderer renderer,
    Func<bool, Action, CancellationToken, Task<InputKey>>? readKey = null)
{
    private const int MaximumVisibleRows = 8;

    public async Task<string?> ChooseAsync(IReadOnlyList<ChildSessionDocument> children, CancellationToken cancellationToken)
    {
        var query = string.Empty;
        var selectedId = renderer.ChildFocus;
        var activeOnly = false;
        var followsRenderer = renderer.ChildSessions.Count > 0;
        IReadOnlyList<ChildSessionDocument> matches = [];
        var selected = 0;
        IReadOnlyList<ChildSessionDocument>? rendered = null;
        var renderedQuery = string.Empty;
        var renderedActiveOnly = false;
        string? renderedSelection = null;
        var renderedWidth = 0;
        var renderedHeight = 0;
        void Refresh()
        {
            var current = renderer.ChildSessions;
            followsRenderer |= current.Count > 0;
            var source = followsRenderer ? current : children;
            var width = ScreenSize.Width;
            var height = ScreenSize.Height;
            if (ReferenceEquals(rendered, source) && renderedQuery == query && renderedActiveOnly == activeOnly
                && renderedSelection == selectedId && renderedWidth == width && renderedHeight == height)
            {
                return;
            }
            matches = Filter(source, query, activeOnly);
            selected = Math.Max(0, matches.ToList().FindIndex(child => child.Id == selectedId));
            selectedId = matches.Count > 0 ? matches[selected].Id : null;
            renderer.SetOverlay(Render(matches, query, selected, renderer.ChildFocus, activeOnly, width, height));
            rendered = source;
            renderedQuery = query;
            renderedActiveOnly = activeOnly;
            renderedSelection = selectedId;
            renderedWidth = width;
            renderedHeight = height;
        }
        renderer.PauseComposer();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Refresh();
                var key = readKey is null
                    ? await renderer.ReadKeyAsync(false, Refresh, cancellationToken)
                    : await readKey(false, Refresh, cancellationToken);
                Refresh();
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                    case ConsoleKey.F7:
                        return null;
                    case ConsoleKey.Enter when matches.Count > 0:
                        return matches[selected].Id;
                    case ConsoleKey.UpArrow when matches.Count > 0:
                        selected = Math.Max(0, selected - 1);
                        selectedId = matches[selected].Id;
                        break;
                    case ConsoleKey.DownArrow when matches.Count > 0:
                        selected = Math.Min(matches.Count - 1, selected + 1);
                        selectedId = matches[selected].Id;
                        break;
                    case ConsoleKey.A when key.Modifiers == ConsoleModifiers.Control:
                        activeOnly = !activeOnly;
                        selectedId = null;
                        break;
                    case ConsoleKey.Backspace when query.Length > 0:
                        query = query[..^1];
                        selectedId = null;
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            query += key.KeyChar;
                            selectedId = null;
                        }
                        break;
                }
            }
        }
        finally
        {
            renderer.ClearOverlay();
            renderer.ResumeComposer();
        }
    }

    internal static IReadOnlyList<ChildSessionDocument> Filter(
        IReadOnlyList<ChildSessionDocument> children, string query, bool activeOnly) => children
        .Where(child => (!activeOnly || IsActive(child))
            && (child.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || child.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                || child.Agent.Contains(query, StringComparison.OrdinalIgnoreCase)
                || child.Status.Contains(query, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(IsActive)
        .ToArray();

    internal static IRenderable Render(IReadOnlyList<ChildSessionDocument> matches, string query, int selected,
        string? currentId, bool activeOnly, int width, int height)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[{Theme.Muted}]Type to filter  Up/Down select  Enter inspect  Esc cancel[/]"),
            new Markup($"[{Theme.Muted}]Ctrl+A: {(activeOnly ? "show all" : "active only")}  {matches.Count} sessions[/]"),
            new Markup($"[{Theme.Accent}]Search > [/]{MarkupText.Escape(query)}")
        };
        if (matches.Count == 0)
        {
            rows.Add(new Markup($"[{Theme.Muted}]No matching child sessions[/]"));
        }
        else
        {
            var visible = Math.Min(MaximumVisibleRows, Math.Max(1, (height - 12) / 2));
            var start = Math.Clamp(selected - visible + 1, 0, Math.Max(0, matches.Count - visible));
            for (var index = start; index < Math.Min(matches.Count, start + visible); index++)
            {
                var child = matches[index];
                var current = child.Id == currentId ? "  current" : string.Empty;
                var label = TextWidth.Truncate($"{child.Label}  {child.Agent}  {child.Status}{current}", Math.Max(8, width - 12));
                var color = index == selected ? Theme.Selected : Theme.User;
                rows.Add(new Markup($"[{color}]{(index == selected ? "> " : "  ")}{MarkupText.Escape(label)}[/]"));
                var usage = $"{child.Id}  {child.Model.Model ?? "Unknown model"}  "
                    + $"IN {child.CumulativeUsage?.InputTokenCount.ToString() ?? "?"} / OUT {child.CumulativeUsage?.OutputTokenCount.ToString() ?? "?"}";
                rows.Add(new Markup($"[{Theme.Muted}]  {MarkupText.Escape(TextWidth.Truncate(usage, Math.Max(8, width - 12)))}[/]"));
            }
        }

        return new Padder(new Panel(new Rows(rows))
        {
            Header = new PanelHeader("Child Sessions"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        }, new Padding(2, 0, 0, 0));
    }

    private static bool IsActive(ChildSessionDocument child) => child.Status is "running" or "queued";
}
