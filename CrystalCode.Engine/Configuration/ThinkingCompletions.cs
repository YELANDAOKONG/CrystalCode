using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Slash argument completions for /thinking, limited to the active model.
/// </summary>
public static class ThinkingCompletions
{
    public static IReadOnlyList<SlashCompletion> For(ModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Thinking)
        {
            return [];
        }

        var options = new List<SlashCompletion>
        {
            new("default", "Provider default", ["default"])
        };
        if (model.ThinkingCanDisable)
        {
            options.Insert(0, new("off", "Disable thinking", ["off", "none"]));
        }

        foreach (var effort in model.ThinkingEfforts)
        {
            options.Add(new(effort, ThinkingLabel.For(new ThinkingSelection(effort)), [effort]));
        }

        return options;
    }
}
