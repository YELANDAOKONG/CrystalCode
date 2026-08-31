using CrystalCode.Configuration;

namespace CrystalCode.Home;

internal static class SettingsMapper
{
    public static IReadOnlyList<ProviderDefinition> ReadProviders(
        Dictionary<string, ProviderDocument>? document)
    {
        if (document is null || document.Count == 0)
        {
            return [];
        }

        var providers = new List<ProviderDefinition>();
        foreach (var (name, entry) in document)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(entry);
            providers.Add(ReadProvider(name, entry));
        }

        return providers;
    }

    public static Dictionary<string, ProviderDocument> WriteProviders(ProviderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var document = new Dictionary<string, ProviderDocument>(StringComparer.Ordinal);
        foreach (var (name, provider) in catalog.Providers)
        {
            document[name] = WriteProvider(provider);
        }

        return document;
    }

    private static ProviderDefinition ReadProvider(string name, ProviderDocument entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Protocol))
        {
            throw new InvalidOperationException(
                $"Provider '{name}' is missing protocol.");
        }

        if (string.IsNullOrWhiteSpace(entry.BaseUri)
            || !Uri.TryCreate(entry.BaseUri, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                $"Provider '{name}' is missing an absolute baseUri.");
        }

        var models = new Dictionary<string, ModelSettings>(StringComparer.Ordinal);
        if (entry.Models is not null)
        {
            foreach (var (model, modelEntry) in entry.Models)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(model);
                ArgumentNullException.ThrowIfNull(modelEntry);
                if (modelEntry.ContextWindow is not { } contextWindow)
                {
                    throw new InvalidOperationException(
                        $"Model '{model}' on provider '{name}' is missing contextWindow.");
                }

                models[model] = new ModelSettings(
                    contextWindow,
                    modelEntry.Temperature,
                    modelEntry.TopP,
                    modelEntry.MaxTokens,
                    modelEntry.Thinking ?? false,
                    modelEntry.ThinkingEfforts);
            }
        }

        return new ProviderDefinition(
            ProviderName.Parse(name),
            ProviderProtocol.Parse(entry.Protocol),
            baseUri,
            models,
            entry.Organization,
            entry.Project,
            entry.ReplayReasoningContent ?? false,
            string.IsNullOrWhiteSpace(entry.TokenLimit)
                ? null
                : TokenLimitStyle.Parse(entry.TokenLimit),
            entry.ApiKeyEnvironment,
            entry.ApiKey);
    }

    private static ProviderDocument WriteProvider(ProviderDefinition provider)
    {
        var models = new Dictionary<string, ModelDocument>(StringComparer.Ordinal);
        foreach (var (model, settings) in provider.Models)
        {
            models[model] = new ModelDocument
            {
                ContextWindow = settings.ContextWindow,
                Temperature = settings.Temperature,
                TopP = settings.TopP,
                MaxTokens = settings.MaxTokens,
                Thinking = settings.Thinking ? true : null,
                ThinkingEfforts = settings.ThinkingEfforts.Count == 0
                    ? null
                    : [.. settings.ThinkingEfforts]
            };
        }

        return new ProviderDocument
        {
            Protocol = provider.Protocol.Value,
            BaseUri = provider.BaseUri.AbsoluteUri,
            Organization = provider.Organization,
            Project = provider.Project,
            ReplayReasoningContent = provider.ReplayReasoningContent,
            TokenLimit = provider.TokenLimit.Value,
            ApiKeyEnvironment = provider.ApiKeyEnvironment,
            ApiKey = provider.ApiKey,
            Models = models
        };
    }
}
