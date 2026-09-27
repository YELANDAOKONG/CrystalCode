using System.Collections.ObjectModel;

namespace CrystalCode.Configuration;

/// <summary>Groups endpoints by provider name and routes models to their protocols.</summary>
public sealed class ProviderCatalog
{
    private readonly Dictionary<string, IReadOnlyList<ProviderDefinition>> _providers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ProviderDefinition>> _readOnlyProviders;

    public ProviderCatalog(IEnumerable<ProviderDefinition> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = new Dictionary<string, IReadOnlyList<ProviderDefinition>>(StringComparer.Ordinal);
        _readOnlyProviders = new ReadOnlyDictionary<string, IReadOnlyList<ProviderDefinition>>(
            _providers);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            Add(provider, overlay: false);
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ProviderDefinition>> Providers =>
        _readOnlyProviders;

    public static ProviderCatalog CreateStarter() => new(
    [
        new ProviderDefinition(
            ProviderName.DeepSeek,
            ProviderProtocol.DeepSeek,
            new Uri("https://api.deepseek.com/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["deepseek-flash"] = new(
                    1_000_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "maximum"],
                    imageInput: true),
                ["deepseek-v4-flash"] = new(
                    1_000_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "maximum"],
                    imageInput: true),
                ["deepseek-v4-pro"] = new(
                    1_000_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "maximum"])
            }),
        new ProviderDefinition(
            ProviderName.OpenAI,
            ProviderProtocol.OpenAI,
            new Uri("https://api.openai.com/v1/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["gpt-5.6-sol"] = new(400_000, imageInput: true),
                ["gpt-5.6-terra"] = new(400_000, imageInput: true),
                ["gpt-5.6-luna"] = new(400_000, imageInput: true)
            })
    ]);

    public ProviderCatalog Overlay(IEnumerable<ProviderDefinition> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var merged = new ProviderCatalog(_providers.Values.SelectMany(static variants => variants));
        var replacementProtocols = new HashSet<(string Name, string Protocol)>();
        var replacementModels = new HashSet<(string Name, string Model)>();
        foreach (var replacement in replacements)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            foreach (var model in replacement.Models.Keys)
            {
                if (!replacementModels.Add((replacement.Name.Value, model)))
                {
                    throw new InvalidOperationException(
                        $"Model '{model}' is configured more than once for provider "
                        + $"'{replacement.Name.Value}'. Use a unique model id for each protocol.");
                }
            }

            if (!replacementProtocols.Add((replacement.Name.Value, replacement.Protocol.Value)))
            {
                throw new InvalidOperationException(
                    $"Protocol '{replacement.Protocol.Value}' is configured more than once "
                    + $"for provider '{replacement.Name.Value}'.");
            }

            merged.Add(replacement, overlay: true);
        }

        return merged;
    }

    public IReadOnlyList<ProviderDefinition> Get(ProviderName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_providers.TryGetValue(name.Value, out var variants))
        {
            return variants;
        }

        throw new KeyNotFoundException(
            $"Provider '{name.Value}' is not configured. Add it under providers in config.json.");
    }

    public IReadOnlyList<string> GetModelNames(ProviderName name) =>
        Get(name).SelectMany(static variant => variant.Models.Keys).ToArray();

    public ProviderDefinition GetModelProvider(ProviderName name, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        foreach (var variant in Get(name))
        {
            if (variant.TryGetModel(model, out _))
            {
                return variant;
            }
        }

        throw new KeyNotFoundException(
            $"Model '{model}' is not configured for provider '{name.Value}'. "
            + "Add it under that provider's models, including contextWindow.");
    }

    public ModelSettings GetModel(ProviderName name, string model) =>
        GetModelProvider(name, model).Models[model];

    private void Add(ProviderDefinition provider, bool overlay)
    {
        var name = provider.Name.Value;
        if (!_providers.TryGetValue(name, out var existing))
        {
            _providers[name] = new List<ProviderDefinition> { provider }.AsReadOnly();
            return;
        }

        var variants = existing.ToList();
        var sameProtocol = variants.FindIndex(variant => variant.Protocol == provider.Protocol);
        if (sameProtocol >= 0 && overlay)
        {
            variants[sameProtocol] = Merge(variants[sameProtocol], provider);
        }
        else
        {
            variants.Add(provider);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            foreach (var model in variant.Models.Keys)
            {
                if (!names.Add(model))
                {
                    throw new InvalidOperationException(
                        $"Model '{model}' is configured more than once for provider '{name}'. "
                        + "Use a unique model id for each protocol.");
                }
            }
        }

        _providers[name] = variants.AsReadOnly();
    }

    private static ProviderDefinition Merge(
        ProviderDefinition existing,
        ProviderDefinition replacement)
    {
        var models = new Dictionary<string, ModelSettings>(existing.Models, StringComparer.Ordinal);
        foreach (var (model, settings) in replacement.Models)
        {
            models[model] = settings;
        }

        return new ProviderDefinition(
            replacement.Name,
            replacement.Protocol,
            replacement.BaseUri,
            models,
            replacement.Organization ?? existing.Organization,
            replacement.Project ?? existing.Project,
            replacement.ReplayReasoningContent,
            replacement.TokenLimit,
            replacement.ApiKeyEnvironment ?? existing.ApiKeyEnvironment,
            replacement.ApiKey ?? existing.ApiKey);
    }
}
