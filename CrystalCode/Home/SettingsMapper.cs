using System.Text.Json;

using CrystalCode.Configuration;

namespace CrystalCode.Home;

internal static class SettingsMapper
{
    public static IReadOnlyList<ProviderDefinition> ReadProviders(
        JsonElement? document)
    {
        if (document is null || document.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (document.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Providers must be an object.");
        }

        var providers = new List<ProviderDefinition>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.Value.EnumerateObject())
        {
            var name = ProviderName.Parse(property.Name).Value;
            if (!names.Add(name))
            {
                throw new InvalidOperationException(
                    $"Provider '{name}' is listed more than once. Use an array under one name.");
            }

            var entry = property.Value;
            if (entry.ValueKind == JsonValueKind.Array)
            {
                if (entry.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException(
                        $"Provider '{name}' must contain at least one protocol.");
                }

                foreach (var variant in entry.EnumerateArray())
                {
                    providers.Add(ReadProvider(name, ReadDocument(name, variant)));
                }
            }
            else
            {
                providers.Add(ReadProvider(name, ReadDocument(name, entry)));
            }
        }

        return providers;
    }

    public static JsonElement WriteProviders(ProviderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var document = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, variants) in catalog.Providers)
        {
            document[name] = variants.Count == 1
                ? JsonSerializer.SerializeToElement(WriteProvider(variants[0]), HomeJson.Options)
                : JsonSerializer.SerializeToElement(
                    variants.Select(WriteProvider).ToArray(),
                    HomeJson.Options);
        }

        return JsonSerializer.SerializeToElement(document, HomeJson.Options);
    }

    private static ProviderDocument ReadDocument(string name, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Provider '{name}' must be an object or an array of objects.");
        }

        var modelNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in entry.EnumerateObject())
        {
            if (!string.Equals(property.Name, "models", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var model in property.Value.EnumerateObject())
            {
                if (!modelNames.Add(model.Name))
                {
                    throw new InvalidOperationException(
                        $"Model '{model.Name}' is configured more than once "
                        + $"for provider '{name}'.");
                }
            }
        }

        return entry.Deserialize<ProviderDocument>(HomeJson.Options)
            ?? throw new InvalidOperationException($"Provider '{name}' is invalid.");
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
                    modelEntry.ThinkingEfforts,
                    modelEntry.ImageInput ?? false);
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
            entry.ApiKey,
            entry.RequiresApiKey);
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
                    : [.. settings.ThinkingEfforts],
                ImageInput = settings.ImageInput ? true : null
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
            RequiresApiKey = provider.RequiresApiKey ==
                (provider.Protocol != ProviderProtocol.Ollama)
                    ? null
                    : provider.RequiresApiKey,
            Models = models
        };
    }
}
