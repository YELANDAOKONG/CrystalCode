using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Clients;
using CrystalCode.Plugins.Commands;
using CrystalCode.Plugins.Hooks;
using CrystalCode.Plugins.Placeholders;
using CrystalCode.Plugins.Tools;

namespace CrystalCode.Plugins;

/// <summary>
/// Tools, protocol factories, classifiers, slash commands, hooks, raw hooks,
/// and prompt placeholders from one plugin.
/// </summary>
public sealed record PluginContribution
{
    public PluginContribution(
        IEnumerable<IPluginTool>? tools = null,
        IEnumerable<IPluginClientFactory>? clients = null,
        IEnumerable<IPluginClassifier>? classifiers = null,
        IEnumerable<IPluginCommand>? commands = null,
        IEnumerable<IPluginHook>? hooks = null,
        IEnumerable<IPluginRawHook>? rawHooks = null,
        IEnumerable<IPluginPlaceholder>? placeholders = null)
    {
        Tools = [.. tools ?? []];
        Clients = [.. clients ?? []];
        Classifiers = [.. classifiers ?? []];
        Commands = [.. commands ?? []];
        Hooks = [.. hooks ?? []];
        RawHooks = [.. rawHooks ?? []];
        Placeholders = [.. placeholders ?? []];
    }

    public IReadOnlyList<IPluginTool> Tools { get; }

    public IReadOnlyList<IPluginClientFactory> Clients { get; }

    public IReadOnlyList<IPluginClassifier> Classifiers { get; }

    public IReadOnlyList<IPluginCommand> Commands { get; }

    public IReadOnlyList<IPluginHook> Hooks { get; }

    /// <summary>Privileged hooks. The host does not hold them to the rules that bind <see cref="Hooks"/>.</summary>
    public IReadOnlyList<IPluginRawHook> RawHooks { get; }

    public IReadOnlyList<IPluginPlaceholder> Placeholders { get; }

    public override string ToString() => nameof(PluginContribution);
}
