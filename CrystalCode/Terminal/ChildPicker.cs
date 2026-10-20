using Spectre.Console;

using CrystalCode.Engine.Home;

namespace CrystalCode.Terminal;

internal sealed class ChildPicker(SessionRenderer renderer)
{
    public async Task<string?> ChooseAsync(IReadOnlyList<ChildSessionDocument> children, CancellationToken cancellationToken)
    {
        var query = string.Empty;
        var selected = 0;
        renderer.PauseComposer();
        try
        {
            while (true)
            {
                var matches = children.Where(child =>
                    (child.Id + " " + child.Label + " " + child.Agent).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
                selected = Math.Clamp(selected, 0, Math.Max(0, matches.Length - 1));
                var first = Math.Max(0, selected - 7);
                var lines = new List<string> { "Type to filter  Up/Down select  Enter inspect  Esc cancel", "Search > " + query };
                for (var index = first; index < Math.Min(first + 8, matches.Length); index++)
                {
                    var child = matches[index];
                    lines.Add($"{(index == selected ? ">" : " ")} {child.Label}  {child.Agent}  {child.Status}  "
                        + $"input {child.CumulativeUsage?.InputTokenCount.ToString() ?? "?"} / output {child.CumulativeUsage?.OutputTokenCount.ToString() ?? "?"}");
                    lines.Add("  " + child.Id);
                }
                renderer.SetOverlay(new Panel(new Text(string.Join("\n", lines))) { Header = new PanelHeader("Child Sessions"), Expand = true });
                var key = await renderer.ReadKeyAsync(scrollPlainArrows: false, cancellationToken);
                switch (key.Key)
                {
                    case ConsoleKey.Escape: return null;
                    case ConsoleKey.Enter when matches.Length > 0: return matches[selected].Id;
                    case ConsoleKey.UpArrow: selected = Math.Max(0, selected - 1); break;
                    case ConsoleKey.DownArrow: selected = Math.Min(matches.Length - 1, selected + 1); break;
                    case ConsoleKey.Backspace when query.Length > 0: query = query[..^1]; selected = 0; break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            query += key.KeyChar;
                            selected = 0;
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
}
