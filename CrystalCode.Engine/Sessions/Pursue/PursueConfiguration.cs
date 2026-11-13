using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>The saved checker choices and limits, also available without an objective.</summary>
public sealed record PursueConfiguration(PursueModelSettings Model, PursueLimits Limits);
