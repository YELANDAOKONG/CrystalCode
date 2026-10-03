using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A dedicated stats page requested by <c>/stats</c>.
/// </summary>
public sealed record StatsReported(SessionStatsReport Report, bool AllWorkspaces) : SessionEvent;
