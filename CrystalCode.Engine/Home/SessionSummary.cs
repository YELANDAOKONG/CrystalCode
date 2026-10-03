namespace CrystalCode.Engine.Home;

/// <summary>
/// Read-only metadata for one resumable session.
/// </summary>
public sealed record SessionSummary(
    string Id,
    string Workspace,
    bool PlanMode,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc,
    int UserTurns,
    string Preview);
