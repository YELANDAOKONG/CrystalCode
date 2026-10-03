using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The operator asked for help. Built-in verbs come from <c>SlashCatalog</c>.
/// </summary>
public sealed record HelpRequested(IReadOnlyList<ISlashCommand> PluginCommands) : SessionEvent;
