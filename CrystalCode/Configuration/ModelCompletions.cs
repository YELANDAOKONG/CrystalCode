using CrystalCode.Display.Composer;

namespace CrystalCode.Configuration;

/// <summary>
/// Slash argument completions for /model: current-provider models, then
/// each provider with nested model names.
/// </summary>
public static class ModelCompletions
{
    public static IReadOnlyList<SlashOption> For(
        ProviderCatalog catalog,
        ProviderName currentProvider)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(currentProvider);

        var options = new List<SlashOption>();
        var current = catalog.Get(currentProvider);
        foreach (var model in current.Models.Keys)
        {
            options.Add(new SlashOption(model, currentProvider.Value, [model]));
        }

        foreach (var (providerName, provider) in catalog.Providers)
        {
            var nested = new List<SlashOption>(provider.Models.Count);
            foreach (var model in provider.Models.Keys)
            {
                nested.Add(new SlashOption(model, providerName, [model]));
            }

            options.Add(new SlashOption(providerName, "provider", [providerName], nested));
        }

        return options;
    }
}
