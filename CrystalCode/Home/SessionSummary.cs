namespace CrystalCode.Home;

/// <summary>
/// Read-only metadata for one resumable session.
/// </summary>
internal sealed record SessionSummary(
    string Id,
    string Workspace,
    bool PlanMode,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc,
    int UserTurns,
    string Preview);
