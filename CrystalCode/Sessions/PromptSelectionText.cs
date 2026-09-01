using CrystalCode.Prompts;

namespace CrystalCode.Sessions;

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
        foreach (var name in resolution.AvailableSets)
        {
            var marker = string.Equals(name, resolution.PromptSet, StringComparison.Ordinal)
                ? "* "
                : "  ";
            lines.Add(marker + name);
        }

        lines.Add(string.Empty);
        lines.Add("Effective Prompts:");
        lines.Add("  Work    " + Source(resolution.WorkSource, resolution.PromptSet));
        lines.Add("  Plan    " + Source(resolution.PlanSource, resolution.PromptSet));
        lines.Add("  Review  " + Source(resolution.ReviewSource, resolution.PromptSet));
        return string.Join(Environment.NewLine, lines);
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
