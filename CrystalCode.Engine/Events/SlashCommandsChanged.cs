using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The completable command menu changed, for example after a model switch.
/// </summary>
public sealed record SlashCommandsChanged(IReadOnlyList<SlashCompletion> Commands) : SessionEvent;
