using CrystalCode.Plugins.Approvals;
using CrystalCode.Plugins.Approvals.Interfaces;
using CrystalCode.Plugins.Cli;
using CrystalCode.Plugins.Cli.Interfaces;
using CrystalCode.Plugins.Clients;
using CrystalCode.Plugins.Clients.Interfaces;
using CrystalCode.Plugins.Commands;
using CrystalCode.Plugins.Commands.Interfaces;
using CrystalCode.Plugins.Hooks;
using CrystalCode.Plugins.Hooks.Interfaces;
using CrystalCode.Plugins.Placeholders;
using CrystalCode.Plugins.Placeholders.Interfaces;
using CrystalCode.Plugins.Status;
using CrystalCode.Plugins.Status.Interfaces;
using CrystalCode.Plugins.Syntax;
using CrystalCode.Plugins.Syntax.Interfaces;
using CrystalCode.Plugins.Tools;
using CrystalCode.Plugins.Tools.Interfaces;

namespace CrystalCode.Plugins;

/// <summary>
/// Tools, protocol factories, classifiers, slash commands, hooks, raw hooks,
/// prompt placeholders, custom status fields, and process commands from one plugin.
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

    /// <summary>
    /// Status fields added with <see cref="WithStatusFields"/>. The original
    /// constructor leaves this empty so an already compiled plugin keeps
    /// calling that constructor.
    /// </summary>
    public IReadOnlyList<IPluginStatusField> StatusFields { get; init; } = [];

    /// <summary>
    /// Process commands added with <see cref="WithCliCommands"/>. Like
    /// <see cref="StatusFields"/>, the original constructor leaves this empty.
    /// </summary>
    public IReadOnlyList<IPluginCliCommand> CliCommands { get; init; } = [];

    public IReadOnlyList<IPluginTokenizer> Tokenizers { get; init; } = [];

    public PluginContribution WithTokenizers(IEnumerable<IPluginTokenizer> tokenizers)
    {
        ArgumentNullException.ThrowIfNull(tokenizers);
        return this with { Tokenizers = tokenizers.ToArray() };
    }

    public PluginContribution WithCliCommands(IEnumerable<IPluginCliCommand> cliCommands)
    {
        ArgumentNullException.ThrowIfNull(cliCommands);
        return this with { CliCommands = cliCommands.ToArray() };
    }

    public PluginContribution WithStatusFields(IEnumerable<IPluginStatusField> statusFields)
    {
        ArgumentNullException.ThrowIfNull(statusFields);
        return this with { StatusFields = statusFields.ToArray() };
    }

    public override string ToString() => nameof(PluginContribution);
}
