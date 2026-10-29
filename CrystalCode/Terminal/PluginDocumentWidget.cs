using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Terminal;

/// <summary>
/// Rounded card for a document a plugin slash command wrote. Every string
/// is escaped, so plugin text is never read as markup.
/// </summary>
internal static class PluginDocumentWidget
{
    public static IRenderable Create(TranscriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rows = new List<IRenderable>();
        foreach (var block in document.Blocks)
        {
            if (rows.Count > 0)
            {
                rows.Add(new Markup(" "));
            }

            rows.Add(Render(block));
        }

        IRenderable content = rows.Count == 0
            ? new Markup($"[{Theme.Muted}]Nothing to show.[/]")
            : new Rows(rows);
        var panel = new Panel(content)
        {
            Header = new PanelHeader($"[{Theme.Accent}]{MarkupText.Escape(document.Title)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static IRenderable Render(TranscriptDocumentBlock block) =>
        block.Kind switch
        {
            "paragraph" => new Markup(
                $"[{Theme.Chrome}]{MarkupText.Escape(string.Join('\n', block.Lines))}[/]"),
            "keyValues" => KeyValues(block),
            "table" => Table(block),
            "list" => WithOmitted(
                new Rows(block.Lines.Select(static item =>
                    new Markup($"[{Theme.Chrome}]{MarkupText.Escape("- " + item)}[/]"))),
                block.Omitted),
            _ => new Markup(string.Empty)
        };

    private static IRenderable KeyValues(TranscriptDocumentBlock block)
    {
        var grid = new Grid().AddColumn().AddColumn();
        foreach (var row in block.Rows)
        {
            grid.AddRow(
                new Markup($"[{Theme.Muted}]{MarkupText.Escape(row[0])}[/]"),
                new Markup($"[{Theme.Chrome}]{MarkupText.Escape(row[1])}[/]"));
        }

        return WithOmitted(grid, block.Omitted);
    }

    private static IRenderable Table(TranscriptDocumentBlock block)
    {
        var table = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule)
        };
        foreach (var column in block.Columns)
        {
            table.AddColumn(new TableColumn($"[{Theme.Accent}]{MarkupText.Escape(column)}[/]"));
        }

        foreach (var row in block.Rows)
        {
            table.AddRow(row.Select(static cell =>
                (IRenderable)new Markup($"[{Theme.Chrome}]{MarkupText.Escape(cell)}[/]")));
        }

        return WithOmitted(table, block.Omitted);
    }

    private static IRenderable WithOmitted(IRenderable body, int omitted) =>
        omitted == 0
            ? body
            : new Rows(body, new Markup($"[{Theme.Muted}]({omitted} more omitted)[/]"));
}
