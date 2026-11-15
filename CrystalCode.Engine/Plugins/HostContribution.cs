using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// What a contribution assembly hands the host once at startup: the provider
/// definitions that ship with the product, the in-process plugins that serve
/// them, and optionally the model a fresh configuration selects. The engine
/// holds none of this itself.
/// </summary>
public sealed record HostContribution
{
    public HostContribution(
        string key,
        IEnumerable<ProviderDefinition>? providers = null,
        IEnumerable<Func<IPlugin>>? plugins = null,
        ProviderName? defaultProvider = null,
        string? defaultModel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if ((defaultProvider is null) != string.IsNullOrWhiteSpace(defaultModel))
        {
            throw new ArgumentException(
                "A default provider and a default model are given together.",
                nameof(defaultModel));
        }

        Key = key.Trim();
        Providers = [.. providers ?? []];
        Plugins = [.. plugins ?? []];
        DefaultProvider = defaultProvider;
        DefaultModel = defaultModel?.Trim();
    }

    public string Key { get; }

    public IReadOnlyList<ProviderDefinition> Providers { get; }

    /// <summary>
    /// Gets plugin factories. Each plugin registry gets its own instances.
    /// </summary>
    public IReadOnlyList<Func<IPlugin>> Plugins { get; }

    public ProviderName? DefaultProvider { get; }

    public string? DefaultModel { get; }

    public override string ToString() => Key;
}
