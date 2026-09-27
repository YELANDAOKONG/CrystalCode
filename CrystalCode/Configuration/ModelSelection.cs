namespace CrystalCode.Configuration;

/// <summary>
/// Resolves <c>/model</c> arguments against a provider catalog.
/// One token is a model on the current provider (or a unique catalog
/// name, or a provider with a single model). Two tokens are
/// <c>provider</c> then an opaque model id.
/// </summary>
public sealed record ModelSelection(ProviderName Provider, string Model)
{
    public static bool TryResolve(
        ProviderCatalog catalog,
        ProviderName currentProvider,
        string argument,
        out ModelSelection? selection,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(currentProvider);
        selection = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            error = "Pass a model on the current provider, or /model <provider> <model>.";
            return false;
        }

        var trimmed = argument.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0
            ? TryResolveOne(catalog, currentProvider, trimmed, out selection, out error)
            : TryResolveTwo(
                catalog,
                trimmed[..space],
                trimmed[(space + 1)..].Trim(),
                out selection,
                out error);
    }

    public static string Format(ProviderName provider, string model)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return provider.Value + " / " + model.Trim();
    }

    public static string FormatCatalog(
        ProviderCatalog catalog,
        ProviderName currentProvider,
        string currentModel)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(currentProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentModel);

        var blocks = new List<string>();
        foreach (var providerName in catalog.Providers.Keys)
        {
            var lines = new List<string> { providerName };
            foreach (var model in catalog.GetModelNames(new ProviderName(providerName)))
            {
                var current = providerName == currentProvider.Value
                    && string.Equals(model, currentModel, StringComparison.Ordinal);
                lines.Add(current ? "  " + model + "  (current)" : "  " + model);
            }

            blocks.Add(string.Join('\n', lines));
        }

        return string.Join("\n\n", blocks);
    }

    private static bool TryResolveOne(
        ProviderCatalog catalog,
        ProviderName currentProvider,
        string token,
        out ModelSelection? selection,
        out string error)
    {
        selection = null;
        error = string.Empty;
        if (catalog.GetModelNames(currentProvider).Contains(token, StringComparer.Ordinal))
        {
            selection = new ModelSelection(currentProvider, token);
            return true;
        }

        var matches = FindModels(catalog, token);
        if (matches.Count == 1)
        {
            selection = matches[0];
            return true;
        }

        if (matches.Count > 1)
        {
            error =
                $"Model '{token}' is configured on more than one provider. "
                + "Pass /model <provider> <model>.";
            return false;
        }

        if (ProviderName.TryParse(token, out var providerName)
            && providerName is not null
            && catalog.Providers.ContainsKey(providerName.Value))
        {
            return TrySelectProvider(
                providerName,
                catalog.GetModelNames(providerName),
                out selection,
                out error);
        }

        error =
            $"Model '{token}' is not configured. "
            + "Pass a model on the current provider, or /model <provider> <model>.";
        return false;
    }

    private static bool TryResolveTwo(
        ProviderCatalog catalog,
        string providerToken,
        string model,
        out ModelSelection? selection,
        out string error)
    {
        selection = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(model))
        {
            error = "Pass /model <provider> <model>.";
            return false;
        }

        if (!ProviderName.TryParse(providerToken, out var providerName)
            || providerName is null
            || !catalog.Providers.ContainsKey(providerName.Value))
        {
            error =
                $"Provider '{providerToken.Trim()}' is not configured. "
                + "Add it under providers in config.json.";
            return false;
        }

        if (!catalog.GetModelNames(providerName).Contains(model, StringComparer.Ordinal))
        {
            error =
                $"Model '{model}' is not configured for provider '{providerName.Value}'. "
                + "Add it under that provider's models, including contextWindow.";
            return false;
        }

        selection = new ModelSelection(providerName, model);
        return true;
    }

    private static bool TrySelectProvider(
        ProviderName providerName,
        IReadOnlyList<string> models,
        out ModelSelection? selection,
        out string error)
    {
        selection = null;
        error = string.Empty;
        if (models.Count == 1)
        {
            selection = new ModelSelection(providerName, models[0]);
            return true;
        }

        if (models.Count == 0)
        {
            error = $"Provider '{providerName.Value}' has no models configured.";
            return false;
        }

        error =
            $"Provider '{providerName.Value}' has more than one model. "
            + $"Pass /model {providerName.Value} <model>.";
        return false;
    }

    private static List<ModelSelection> FindModels(ProviderCatalog catalog, string token)
    {
        var matches = new List<ModelSelection>();
        foreach (var name in catalog.Providers.Keys)
        {
            if (catalog.GetModelNames(new ProviderName(name)).Contains(token, StringComparer.Ordinal))
            {
                matches.Add(new ModelSelection(new ProviderName(name), token));
            }
        }

        return matches;
    }

    public override string ToString() => Format(Provider, Model);
}
