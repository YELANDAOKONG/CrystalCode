namespace CrystalCode.Engine.Home;

/// <summary>
/// Read-only metadata for one resumable session. <see cref="Preview"/> is the
/// first user message; <see cref="Topic"/> is the session topic, or null.
/// </summary>
public sealed record SessionSummary(
    string Id,
    string Workspace,
    bool PlanMode,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc,
    int UserTurns,
    string Preview,
    string? Mode = null,
    string? Topic = null)
{
    /// <summary>The topic when there is one, otherwise the first user message.</summary>
    public string Title => Topic ?? Preview;
}
