namespace CrystalCode.Engine.Events;

/// <summary>
/// The selectable session settings and their current values: mode, model,
/// thinking, context budget, tier, speed, approval, the approval and extended
/// approval models, prompt set, Agents, microcompaction, and estimated tokens.
/// Published at start and whenever one of them or its choices changed.
/// </summary>
public sealed record SessionOptionsChanged(IReadOnlyList<SessionOption> Options) : SessionEvent;
