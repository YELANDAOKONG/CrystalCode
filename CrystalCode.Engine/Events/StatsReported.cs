namespace CrystalCode.Engine.Events;

/// <summary>
/// A dedicated stats page requested by <c>/stats</c>.
/// </summary>
public sealed record StatsReported(string Text) : SessionEvent;
