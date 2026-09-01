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
        var lines = new List<string>
        {
            "Tools",
            $"External: {(settings.ExternalTools ? "On" : "Off")}",
            $"Approval: Home {Title(settings.ExternalToolApproval.Home.Value)}, Project {Title(settings.ExternalToolApproval.Project.Value)}",
            string.Empty
        };

        foreach (var definition in definitions)
        {
            var catalogs = Catalogs(planNames.Contains(definition.Name), workNames.Contains(definition.Name));
            if (externalByName.TryGetValue(definition.Name, out var info))
            {
                lines.Add(
                    $"{definition.Name}  {Title(info.Source.Value)}:{info.SetName}  {catalogs}  "
                    + $"Author {Title(info.DeclaredApproval.Value)}  Effective {Title(info.EffectiveApproval.Value)}");
            }
            else
            {
                lines.Add($"{definition.Name}  Host  {catalogs}  Effective Host");
            }
        }

        if (definitions.Length == 0)
        {
            lines.Add("No tools are loaded.");
        }

        if (external.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("External tool notes");
            lines.AddRange(external.Notes.Select(note => "- " + note));
        }

        lines.Add(string.Empty);
        lines.Add("Manage: /tools on|off|reload, /tools home author|host, /tools project author|host");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatApproval(ExternalToolApprovalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return $"tool approval  Home {Title(settings.Home.Value)}, Project {Title(settings.Project.Value)}";
    }

    private static string Catalogs(bool plan, bool work)
    {
        if (plan && work)
        {
            return "Plan+Work";
        }

        return plan ? "Plan" : "Work";
    }

    private static string Title(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];
}
