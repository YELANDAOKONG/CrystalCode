namespace CrystalHarness.Configuration;

/// <summary>
/// Merged built-in and user-defined provider entries.
/// </summary>
public sealed class ProviderCatalog
{
    private readonly Dictionary<string, ProviderDefinition> _providers;

    public ProviderCatalog(IEnumerable<ProviderDefinition> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = new Dictionary<string, ProviderDefinition>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            _providers[provider.Name.Value] = provider;
        }
    }

    public IReadOnlyDictionary<string, ProviderDefinition> Providers => _providers;

    public static ProviderCatalog CreateStarter()
    {
        return new ProviderCatalog(
        [
            new ProviderDefinition(
                ProviderName.DeepSeek,
                ProviderProtocol.DeepSeek,
                new Uri("https://api.deepseek.com/"),
                new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
                {
                    ["deepseek-v4-flash"] = new(1_000_000),
                    ["deepseek-v4-pro"] = new(1_000_000)
                }),
            new ProviderDefinition(
                ProviderName.OpenAI,
                ProviderProtocol.OpenAI,
                new Uri("https://api.openai.com/v1/"),
                new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
                {
                    ["gpt-5.6-sol"] = new(400_000),
                    ["gpt-5.6-terra"] = new(400_000),
                    ["gpt-5.6-luna"] = new(400_000)
                })
        ]);
    }

    public ProviderCatalog Overlay(IEnumerable<ProviderDefinition> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);

        var merged = new Dictionary<string, ProviderDefinition>(
            _providers,
            StringComparer.Ordinal);
        foreach (var replacement in replacements)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            if (merged.TryGetValue(replacement.Name.Value, out var existing))
            {
                merged[replacement.Name.Value] = Merge(existing, replacement);
                continue;
            }

            merged[replacement.Name.Value] = replacement;
        }

        return new ProviderCatalog(merged.Values);
    }

    public ProviderDefinition Get(ProviderName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_providers.TryGetValue(name.Value, out var provider))
        {
            return provider;
        }

        throw new KeyNotFoundException(
            $"Provider '{name.Value}' is not configured. Add it under providers in config.json.");
    }

    public ModelSettings GetModel(ProviderName name, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var provider = Get(name);
        if (provider.TryGetModel(model, out var settings))
        {
            return settings;
        }

        throw new KeyNotFoundException(
            $"Model '{model}' is not configured for provider '{name.Value}'. "
            + "Add it under that provider's models, including contextWindow.");
    }

    private static ProviderDefinition Merge(
        ProviderDefinition existing,
        ProviderDefinition replacement)
    {
        var models = new Dictionary<string, ModelSettings>(
            existing.Models,
            StringComparer.Ordinal);
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
