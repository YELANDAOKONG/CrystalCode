using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The operator asked for help. Built-in verbs come from <c>SlashCatalog</c>;
/// <paramref name="PluginCommands"/> lists plugin verbs by name and help text.
/// </summary>
public sealed record HelpRequested(IReadOnlyList<SlashCompletion> PluginCommands) : SessionEvent;
