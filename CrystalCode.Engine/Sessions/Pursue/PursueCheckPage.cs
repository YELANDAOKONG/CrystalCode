namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>A page of checks belonging to one objective, oldest first.</summary>
public sealed record PursueCheckPage(string? ObjectiveId, IReadOnlyList<PursueCheckRecord> Checks, int NextOffset, bool HasMore);
