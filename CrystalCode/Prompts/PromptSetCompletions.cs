using CrystalCode.Display.Composer;

namespace CrystalCode.Prompts;

internal static class PromptSetCompletions
{
    public static IReadOnlyList<SlashOption> For(PromptResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var options = new List<SlashOption>
        {
            new(PromptSetNames.Default, "no selected prompt set", [PromptSetNames.Default])
        };
        foreach (var name in resolution.AvailableSets)
        {
            options.Add(new SlashOption(name, "prompt set", [name]));
        }

        return options;
    }
}
