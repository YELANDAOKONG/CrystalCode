using Crystal.Tools;

using CrystalCode.Configuration;
using CrystalCode.Tools.External;

namespace CrystalCode.Sessions;

/// <summary>
/// Plain-text catalog rendered by <c>/tools</c>.
/// </summary>
public static class ToolListText
{
    public static string Format(
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
        var hostRows = definitions
            .Where(definition => !externalByName.ContainsKey(definition.Name))
            .Select(
                definition => new[]
                {
                    definition.Name,
                    Catalogs(planNames.Contains(definition.Name), workNames.Contains(definition.Name)),
                    "Host"
                })
            .ToArray();
        var externalRows = definitions
            .Where(definition => externalByName.ContainsKey(definition.Name))
            .Select(
                definition =>
                {
                    var info = externalByName[definition.Name];
                    return new[]
                    {
                        definition.Name,
                        Catalogs(planNames.Contains(definition.Name), workNames.Contains(definition.Name)),
                        $"{Title(info.Source.Value)}/{info.SetName}",
                        Title(info.DeclaredApproval.Value),
                        Title(info.EffectiveApproval.Value)
                    };
                })
            .ToArray();
        var lines = new List<string>
        {
            $"Tools  {definitions.Length} loaded  ·  Plan {plan.Count}  ·  Work {work.Count}",
            string.Empty
        };

        AddTable(lines, $"Host tools ({hostRows.Length})", ["Name", "Catalog", "Approval"], hostRows);
        lines.Add(string.Empty);
        AddTable(
            lines,
            $"External tools ({externalRows.Length}, {(settings.ExternalTools ? "On" : "Off")})",
            ["Name", "Catalog", "Source", "Author", "Effective"],
            externalRows);
        lines.Add(string.Empty);
        AddTable(
            lines,
            "External approval",
            ["Source", "Policy"],
            [
                ["Home", Title(settings.ExternalToolApproval.Home.Value)],
                ["Project", Title(settings.ExternalToolApproval.Project.Value)]
            ]);

        if (external.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("External tool notes");
            lines.AddRange(external.Notes.Select(note => "- " + note));
        }

        lines.Add(string.Empty);
        lines.Add("Commands");
        lines.Add("  /tools on|off|reload");
        lines.Add("  /tools home|project author|host");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatApproval(ExternalToolApprovalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return $"Tool approval  Home {Title(settings.Home.Value)}, Project {Title(settings.Project.Value)}";
    }

    private static string Catalogs(bool plan, bool work)
    {
        if (plan && work)
        {
            return "Plan+Work";
        }

        return plan ? "Plan" : "Work";
    }

    private static void AddTable(
        List<string> lines,
        string title,
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows)
    {
        lines.Add(title);
        if (rows.Count == 0)
        {
            lines.Add("  None");
            return;
        }

        var widths = headers
            .Select((header, index) => Math.Max(header.Length, rows.Max(row => row[index].Length)))
            .ToArray();
        lines.Add("  " + FormatRow(headers, widths));
        foreach (var row in rows)
        {
            lines.Add("  " + FormatRow(row, widths));
        }
    }

    private static string FormatRow(IReadOnlyList<string> values, IReadOnlyList<int> widths) =>
        string.Join(
            "  ",
            values.Select(
                (value, index) => index == values.Count - 1
                    ? value
                    : value.PadRight(widths[index])));

    private static string Title(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];
}
