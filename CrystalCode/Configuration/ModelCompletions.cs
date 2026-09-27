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
        foreach (var model in catalog.GetModelNames(currentProvider))
        {
            options.Add(new SlashOption(model, currentProvider.Value, [model]));
        }

        foreach (var providerName in catalog.Providers.Keys)
        {
            var models = catalog.GetModelNames(new ProviderName(providerName));
            var nested = new List<SlashOption>(models.Count);
            foreach (var model in models)
            {
                nested.Add(new SlashOption(model, providerName, [model]));
            }

            options.Add(new SlashOption(providerName, "Provider", [providerName], nested));
        }

        return options;
    }
}
