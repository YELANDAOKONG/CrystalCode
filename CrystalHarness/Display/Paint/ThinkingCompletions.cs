using CrystalHarness.Configuration;
using CrystalHarness.Display.Composer;

namespace CrystalHarness.Display.Paint;

/// <summary>
/// Slash argument completions for /thinking, limited to the active model.
/// </summary>
public static class ThinkingCompletions
{
    public static IReadOnlyList<SlashOption> For(ModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Thinking)
        {
            return [];
        }

        var options = new List<SlashOption>
        {
            new("off", "disable thinking", ["off", "none"]),
            new("default", "provider default", ["default"])
        };

        foreach (var effort in model.ThinkingEfforts)
        {
            var keys = effort == "maximum"
                ? new[] { "maximum", "max" }
                : new[] { effort };
            options.Add(new(effort, ThinkingLabel.For(new ThinkingSelection(effort)), keys));
        }

        return options;
    }
}
