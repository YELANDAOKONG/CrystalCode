using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Clients;
using CrystalCode.Plugins.Commands;
using CrystalCode.Plugins.Hooks;
using CrystalCode.Plugins.Tools;

namespace CrystalCode.Plugins;

/// <summary>
/// Tools, protocol factories, classifiers, slash commands, and hooks from one plugin.
/// </summary>
public sealed record PluginContribution
{
    public PluginContribution(
        IEnumerable<IPluginTool>? tools = null,
        IEnumerable<IPluginClientFactory>? clients = null,
        IEnumerable<IPluginClassifier>? classifiers = null,
        IEnumerable<IPluginCommand>? commands = null,
        IEnumerable<IPluginHook>? hooks = null)
    {
        Tools = [.. tools ?? []];
        Clients = [.. clients ?? []];
        Classifiers = [.. classifiers ?? []];
        Commands = [.. commands ?? []];
        Hooks = [.. hooks ?? []];
    }

    public IReadOnlyList<IPluginTool> Tools { get; }

    public IReadOnlyList<IPluginClientFactory> Clients { get; }

    public IReadOnlyList<IPluginClassifier> Classifiers { get; }

    public IReadOnlyList<IPluginCommand> Commands { get; }

    public IReadOnlyList<IPluginHook> Hooks { get; }

    public override string ToString() => nameof(PluginContribution);
}
