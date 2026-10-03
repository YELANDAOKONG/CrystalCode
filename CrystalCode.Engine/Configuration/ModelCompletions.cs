using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Slash argument completions for /model: current-provider models, then
/// each provider with nested model names.
/// </summary>
public static class ModelCompletions
{
    public static IReadOnlyList<SlashCompletion> For(
        ProviderCatalog catalog,
        ProviderName currentProvider)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(currentProvider);

        var options = new List<SlashCompletion>();
        foreach (var model in catalog.GetModelNames(currentProvider))
        {
            options.Add(new SlashCompletion(model, currentProvider.Value, [model]));
        }

        foreach (var providerName in catalog.Providers.Keys)
        {
            var models = catalog.GetModelNames(new ProviderName(providerName));
            var nested = new List<SlashCompletion>(models.Count);
            foreach (var model in models)
            {
                nested.Add(new SlashCompletion(model, providerName, [model]));
            }

            options.Add(new SlashCompletion(providerName, "Provider", [providerName], nested));
        }

        return options;
    }
}
