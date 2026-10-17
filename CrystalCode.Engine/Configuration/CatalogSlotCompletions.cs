using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Slash argument completions for one catalog token list.
/// </summary>
public static class CatalogSlotCompletions
{
    public static IReadOnlyList<SlashCompletion> For(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var options = new List<SlashCompletion>(tokens.Count);
        foreach (var token in tokens)
        {
            options.Add(new SlashCompletion(token, DisplayCase.Token(token), [token]));
        }

        return options;
    }
}
