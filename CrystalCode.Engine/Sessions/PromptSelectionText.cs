using CrystalCode.Engine.Prompts;

namespace CrystalCode.Engine.Sessions;

internal static class PromptSelectionText
{
    public static string Format(PromptResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var lines = new List<string>
        {
            "Prompt Set: " + resolution.PromptSet,
            string.Empty,
            string.Equals(resolution.PromptSet, PromptSetNames.Default, StringComparison.Ordinal)
                ? "* default"
                : "  default"
        };
        foreach (var entry in resolution.Sets)
        {
            var marker = entry.Effective ? "* " : "  ";
            lines.Add(marker + entry.Name + Detail(entry.Name, entry.Title, entry.Description));
        }

        lines.Add(string.Empty);
        lines.Add("Effective Prompts:");
        lines.Add("  Work    " + Source(resolution.WorkSource, resolution.PromptSet));
        lines.Add("  Plan    " + Source(resolution.PlanSource, resolution.PromptSet));
        lines.Add("  Review  " + Source(resolution.ReviewSource, resolution.PromptSet));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Detail(string name, string title, string description)
    {
        var showTitle = title.Length > 0
            && !string.Equals(title, name, StringComparison.Ordinal);
        if (!showTitle && description.Length == 0)
        {
            return string.Empty;
        }

        if (!showTitle)
        {
            return "  " + description;
        }

        if (description.Length == 0)
        {
            return "  " + title;
        }

        return "  " + title + "  " + description;
    }

    private static string Source(PromptSource source, string promptSet) =>
        source switch
        {
            PromptSource.BuiltIn => "Built-In",
            PromptSource.PromptSet => "Prompt Set " + promptSet,
            PromptSource.HomeOverride => "Home Override",
            PromptSource.ProjectOverride => "Project Override",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
