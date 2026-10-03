using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;

namespace CrystalCode.Terminal;

internal static class StatsPageWidget
{
    public static IRenderable Create(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<IRenderable>();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
            {
                rows.Add(Text.Empty);
                continue;
            }

            rows.Add(new Markup($"[{Theme.User}]{MarkupText.Escape(line)}[/]"));
        }

        rows.Add(Text.Empty);
        rows.Add(new Markup($"[{Theme.Muted}]Esc or q to close[/]"));
        return new Panel(new Rows(rows))
        {
            Header = new PanelHeader($"[{Theme.Heading}]Stats[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
    }
}
