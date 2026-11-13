using CrystalCode.Display.Composer;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Terminal;

/// <summary>
/// Projects engine slash completions onto the composer's picker options.
/// </summary>
internal static class SlashOptionMap
{
    public static IReadOnlyList<SlashOption> From(IReadOnlyList<SlashCompletion> completions)
    {
        ArgumentNullException.ThrowIfNull(completions);
        var options = new List<SlashOption>(completions.Count);
        foreach (var completion in completions)
        {
            options.Add(From(completion));
        }

        return options.Select(option => option.Name == "pursue"
            ? option with { Arguments = [new SlashOption("view", "Inspect pursue checks and evidence (F9)", ["view"]), .. option.ArgumentOptions] }
            : option).ToArray();
    }

    public static SlashOption From(SlashCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return new SlashOption(
            completion.Name,
            completion.Help,
            completion.Keys,
            completion.Arguments is null ? null : From(completion.Arguments),
            completion.ArgumentsOptional,
            completion.ArgumentsAfterValue is null ? null : From(completion.ArgumentsAfterValue));
    }
}
