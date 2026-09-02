using Spectre.Console;
using Spectre.Console.Rendering;

using Crystal.Tools;

using CrystalCode.Configuration;
using CrystalCode.Display.Paint;
using CrystalCode.Tools.External;

namespace CrystalCode.Sessions;

/// <summary>
/// Rich, width-aware catalog rendered by <c>/tools</c>.
/// </summary>
internal static class ToolListWidget
{
    public static IRenderable Create(
        IReadOnlyList<ToolDefinition> plan,
        IReadOnlyList<ToolDefinition> work,
        ExternalCatalog external,
        HarnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(external);
        ArgumentNullException.ThrowIfNull(settings);

        var planNames = plan.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var workNames = work.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var definitions = plan.Concat(work)
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();
        var externalByName = external.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var host = definitions.Where(tool => !externalByName.ContainsKey(tool.Name)).ToArray();
        var loadedExternal = definitions.Where(tool => externalByName.ContainsKey(tool.Name)).ToArray();
        var blocks = new List<IRenderable>
        {
            new Markup(
                $"[{Theme.Heading}]Tools[/]  [{Theme.Chrome}]"
                + $"{definitions.Length} loaded  ·  Plan {plan.Count}  ·  Work {work.Count}[/]"),
            SectionRule($"Host tools ({host.Length})"),
            HostTable(host, planNames, workNames),
            SectionRule(
                $"External tools ({loadedExternal.Length}, "
                + $"{(settings.ExternalTools ? "On" : "Off")})"),
            ExternalTable(loadedExternal, externalByName, planNames, workNames),
            SectionRule("External approval"),
            ApprovalGrid(settings.ExternalToolApproval)
        };

        if (external.Notes.Count > 0)
        {
            blocks.Add(SectionRule("External tool notes", Theme.Warning));
            foreach (var note in external.Notes)
            {
                blocks.Add(new Markup($"[{Theme.Warning}]- {MarkupText.Escape(note)}[/]"));
            }
        }

        blocks.Add(
            new Markup(
                $"[{Theme.Chrome}]Commands[/]\n"
                + $"[{Theme.User}]  /tools on|off|reload\n"
                + "  /tools home|project author|host[/]"));
        return new Padder(new Rows(blocks), new Padding(2, 0, 0, 0));
    }

    private static IRenderable HostTable(
        IReadOnlyList<ToolDefinition> tools,
        IReadOnlySet<string> plan,
        IReadOnlySet<string> work)
    {
        if (tools.Count == 0)
        {
            return new Markup($"[{Theme.Muted}]None[/]");
        }

        var table = BaseTable("Name", "Catalog", "Approval");
        foreach (var tool in tools)
        {
            table.AddRow(
                Escape(tool.Name),
                Escape(Catalogs(plan.Contains(tool.Name), work.Contains(tool.Name))),
                "Host");
        }

        return table;
    }

    private static IRenderable ExternalTable(
        IReadOnlyList<ToolDefinition> tools,
        IReadOnlyDictionary<string, ExternalToolInfo> information,
        IReadOnlySet<string> plan,
        IReadOnlySet<string> work)
    {
        if (tools.Count == 0)
        {
            return new Markup($"[{Theme.Muted}]None[/]");
        }

        var table = BaseTable("Name", "Catalog", "Source", "Author", "Effective");
        foreach (var tool in tools)
        {
            var info = information[tool.Name];
            table.AddRow(
                Escape(tool.Name),
                Escape(Catalogs(plan.Contains(tool.Name), work.Contains(tool.Name))),
                Escape($"{Title(info.Source.Value)}/{info.SetName}"),
                Escape(Title(info.DeclaredApproval.Value)),
                Escape(Title(info.EffectiveApproval.Value)));
        }

        return table;
    }

    private static Table BaseTable(params string[] columns)
    {
        var table = new Table
        {
            Border = TableBorder.Simple,
            BorderStyle = Style.Parse(Theme.Rule),
            Expand = true
        };
        foreach (var column in columns)
        {
            table.AddColumn(new TableColumn($"[{Theme.Chrome}]{Escape(column)}[/]"));
        }

        return table;
    }

    private static Grid ApprovalGrid(ExternalToolApprovalSettings approval)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        grid.AddRow(
            new Markup($"[{Theme.Chrome}]Home[/]"),
            new Markup($"[{Theme.User}]{Escape(Title(approval.Home.Value))}[/]"));
        grid.AddRow(
            new Markup($"[{Theme.Chrome}]Project[/]"),
            new Markup($"[{Theme.User}]{Escape(Title(approval.Project.Value))}[/]"));
        return grid;
    }

    private static Rule SectionRule(string title, string color = Theme.Accent) =>
        new($"[{color}]{MarkupText.Escape(title)}[/]")
        {
            Style = Style.Parse(Theme.Rule),
            Justification = Justify.Left
        };

    private static string Catalogs(bool plan, bool work) =>
        plan && work ? "Plan+Work" : plan ? "Plan" : "Work";

    private static string Title(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private static string Escape(string value) => MarkupText.Escape(value);
}
