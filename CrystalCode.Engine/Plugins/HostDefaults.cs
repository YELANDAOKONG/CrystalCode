using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// The contributions the host process registered at startup. The executable
/// registers the product's contribution assemblies in <c>Main</c>; a test
/// registers what it needs. Registering a key again replaces that entry.
/// </summary>
public static class HostDefaults
{
    private static readonly object Gate = new();
    private static IReadOnlyList<HostContribution> _contributions = [];

    public static void Register(HostContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (Gate)
        {
            var next = _contributions
                .Where(existing => !string.Equals(existing.Key, contribution.Key, StringComparison.Ordinal))
                .Append(contribution)
                .ToArray();
            _contributions = next;
        }
    }

    /// <summary>
    /// Builds the catalog of the registered provider definitions.
    /// </summary>
    public static ProviderCatalog CreateCatalog() =>
        new(Snapshot().SelectMany(static contribution => contribution.Providers));

    /// <summary>
    /// Gets the provider and model a fresh configuration selects.
    /// </summary>
    public static (ProviderName Provider, string Model) GetDefaultSelection()
    {
        foreach (var contribution in Snapshot())
        {
            if (contribution.DefaultProvider is { } provider && contribution.DefaultModel is { } model)
            {
                return (provider, model);
            }
        }

        throw new InvalidOperationException(
            "No default provider and model were registered by the host.");
    }

    internal static IEnumerable<IPlugin> CreatePlugins() =>
        Snapshot().SelectMany(static contribution => contribution.Plugins).Select(static create => create());

    private static IReadOnlyList<HostContribution> Snapshot()
    {
        lock (Gate)
        {
            return _contributions;
        }
    }
}
