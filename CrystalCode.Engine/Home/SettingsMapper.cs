using System.Text.Json;

using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

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

    /// <summary>
    /// Reads saved context budgets. The stored form is
    /// <c>{"provider": {"model": tokens}}</c>. The older flat
    /// <c>{"provider/model": tokens}</c> form is still accepted; both shapes
    /// may appear in one document while a file is being carried forward.
    /// Values must be positive token counts.
    /// </summary>
    public static IReadOnlyDictionary<ContextSelectionKey, int> ReadContextSelections(
        JsonElement? document)
    {
        var selections = new Dictionary<ContextSelectionKey, int>();
        if (document is null
            || document.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return selections;
        }

        if (document.Value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("contextSelections must be an object.");
        }

        foreach (var entry in document.Value.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object)
            {
                ReadNestedSelections(entry, selections);
            }
            else
            {
                ReadFlatSelection(entry, selections);
            }
        }

        return selections;
    }

    /// <summary>
    /// Writes saved context budgets in the nested form, sorted by provider
    /// and model so the file stays stable across saves.
    /// </summary>
    public static JsonElement WriteContextSelections(
        IReadOnlyDictionary<ContextSelectionKey, int> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var document = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (key, window) in selections
            .OrderBy(pair => pair.Key.Provider.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Model, StringComparer.Ordinal))
        {
            if (!document.TryGetValue(key.Provider.Value, out var models))
            {
                models = new Dictionary<string, int>(StringComparer.Ordinal);
                document[key.Provider.Value] = models;
            }

            models[key.Model] = window;
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
                    modelEntry.ImageInput ?? false,
                    modelEntry.ThinkingCanDisable ?? true,
                    modelEntry.AudioInput ?? false,
                    modelEntry.VideoInput ?? false,
                    modelEntry.ContextWindows);
            }
        }

        var protocol = ProviderProtocol.Parse(entry.Protocol);
        return new ProviderDefinition(
            ProviderName.Parse(name),
            protocol,
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
            entry.RequiresApiKey,
            EndpointOptionsReader.Read(name, protocol, entry.Options));
    }

    private static ProviderDocument WriteProvider(ProviderDefinition provider)
    {
        var models = new Dictionary<string, ModelDocument>(StringComparer.Ordinal);
        foreach (var (model, settings) in provider.Models)
        {
            models[model] = new ModelDocument
            {
                ContextWindow = settings.ContextWindow,
                ContextWindows = settings.ContextWindows.SequenceEqual([settings.ContextWindow])
                    ? null
                    : [.. settings.ContextWindows],
                Temperature = settings.Temperature,
                TopP = settings.TopP,
                MaxTokens = settings.MaxTokens,
                Thinking = settings.Thinking ? true : null,
                ThinkingEfforts = settings.ThinkingEfforts.Count == 0
                    ? null
                    : [.. settings.ThinkingEfforts],
                ImageInput = settings.ImageInput ? true : null,
                ThinkingCanDisable = settings.ThinkingCanDisable ? null : false,
                AudioInput = settings.AudioInput ? true : null,
                VideoInput = settings.VideoInput ? true : null
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
            Options = EndpointOptionsReader.Write(provider.ThinkingDisplay),
            Models = models
        };
    }

    private static void ReadNestedSelections(
        JsonProperty providerEntry,
        Dictionary<ContextSelectionKey, int> selections)
    {
        if (!ProviderName.TryParse(providerEntry.Name, out var provider) || provider is null)
        {
            throw new JsonException(
                $"contextSelections provider '{providerEntry.Name}' is not a valid provider name.");
        }

        foreach (var modelEntry in providerEntry.Value.EnumerateObject())
        {
            var model = modelEntry.Name;
            if (!IsExactModelId(model))
            {
                throw new JsonException(
                    $"contextSelections model '{model}' under '{provider.Value}' "
                    + "must not be blank or padded with whitespace.");
            }

            if (!TryReadWindow(modelEntry.Value, out var window))
            {
                throw new JsonException(
                    $"contextSelections value for '{provider.Value}/{model}' "
                    + "must be a positive token count.");
            }

            AddSelection(selections, new ContextSelectionKey(provider, model), window);
        }
    }

    private static void ReadFlatSelection(
        JsonProperty entry,
        Dictionary<ContextSelectionKey, int> selections)
    {
        var separator = entry.Name.IndexOf('/');
        if (separator <= 0
            || separator == entry.Name.Length - 1
            || !ProviderName.TryParse(entry.Name[..separator], out var provider)
            || provider is null)
        {
            throw new JsonException(
                $"contextSelections key '{entry.Name}' must be provider/model "
                + "with a valid provider name.");
        }

        var model = entry.Name[(separator + 1)..];
        if (!IsExactModelId(model))
        {
            throw new JsonException(
                $"contextSelections model '{model}' under '{provider.Value}' "
                + "must not be blank or padded with whitespace.");
        }

        if (!TryReadWindow(entry.Value, out var window))
        {
            throw new JsonException(
                $"contextSelections value for '{entry.Name}' must be a positive token count.");
        }

        AddSelection(selections, new ContextSelectionKey(provider, model), window);
    }

    private static void AddSelection(
        Dictionary<ContextSelectionKey, int> selections,
        ContextSelectionKey key,
        int window)
    {
        if (!selections.TryAdd(key, window))
        {
            throw new JsonException($"contextSelections lists '{key}' more than once.");
        }
    }

    private static bool IsExactModelId(string model) =>
        model.Length > 0 && string.Equals(model, model.Trim(), StringComparison.Ordinal);

    private static bool TryReadWindow(JsonElement value, out int window)
    {
        window = 0;
        return value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out window)
            && window > 0;
    }
}
