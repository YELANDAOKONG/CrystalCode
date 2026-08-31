using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Plugins;

/// <summary>
/// Tools, client factories, classifiers, and slash commands from one plugin.
/// </summary>
public sealed record PluginContribution
{
    public PluginContribution(
        IEnumerable<IToolContribution>? tools = null,
        IEnumerable<IChatClientFactory>? clients = null,
        IEnumerable<IApprovalClassifier>? classifiers = null,
        IEnumerable<ISlashCommand>? commands = null)
    {
        Tools = [.. tools ?? []];
        Clients = [.. clients ?? []];
        Classifiers = [.. classifiers ?? []];
        Commands = [.. commands ?? []];
    }

    public IReadOnlyList<IToolContribution> Tools { get; }

    public IReadOnlyList<IChatClientFactory> Clients { get; }

    public IReadOnlyList<IApprovalClassifier> Classifiers { get; }

    public IReadOnlyList<ISlashCommand> Commands { get; }

    public override string ToString() => nameof(PluginContribution);
}
