using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Tools, client factories, classifiers, slash commands, and authentication
/// integrations from one plugin.
/// </summary>
public sealed record PluginContribution
{
    public PluginContribution(
        IEnumerable<IToolContribution>? tools = null,
        IEnumerable<IChatClientFactory>? clients = null,
        IEnumerable<IApprovalClassifier>? classifiers = null,
        IEnumerable<ISlashCommand>? commands = null,
        IEnumerable<IAuthenticationIntegration>? integrations = null)
    {
        Tools = [.. tools ?? []];
        Clients = [.. clients ?? []];
        Classifiers = [.. classifiers ?? []];
        Commands = [.. commands ?? []];
        Integrations = [.. integrations ?? []];
    }

    public IReadOnlyList<IAuthenticationIntegration> Integrations { get; }

    public IReadOnlyList<IToolContribution> Tools { get; }

    public IReadOnlyList<IChatClientFactory> Clients { get; }

    public IReadOnlyList<IApprovalClassifier> Classifiers { get; }

    public IReadOnlyList<ISlashCommand> Commands { get; }

    public override string ToString() => nameof(PluginContribution);
}
