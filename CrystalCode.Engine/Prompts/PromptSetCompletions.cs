using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Prompts;

internal static class PromptSetCompletions
{
    public static IReadOnlyList<SlashCompletion> For(PromptResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var options = new List<SlashCompletion>
        {
            new(PromptSetNames.Default, "No selected prompt set", [PromptSetNames.Default]),
            new(
                "export",
                "Export built-in prompt templates; optional directory",
                ["export"],
                [
                    new(".", "Export to the current workspace", ["."]),
                    new("./prompts", "Export to ./prompts", ["./prompts"])
                ],
                ArgumentsOptional: true)
        };
        foreach (var name in resolution.AvailableSets)
        {
            options.Add(new SlashCompletion(name, "Prompt set", [name]));
        }

        return options;
    }
}
