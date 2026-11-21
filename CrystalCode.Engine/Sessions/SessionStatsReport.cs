namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Token and tool totals for the stats page.
/// </summary>
public sealed record SessionStatsReport(
    int Sessions,
    int UserTurns,
    int ModelCalls,
    int ToolCalls,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? TotalTokens,
    long? AverageTokensPerSession,
    long? MedianTokensPerSession,
    int WindowDays,
    DateTimeOffset? Earliest,
    DateTimeOffset? Latest,
    IReadOnlyList<SessionToolCount> TopTools)
{
    public int? TokenTrackedSessions { get; init; }

    public int? ReasoningTrackedSessions { get; init; }

    public int CacheTrackedSessions { get; init; }

    public int? CacheWindowDays { get; init; }

    public SessionCacheUsage? CacheUsage { get; init; }

    /// <summary>Cache usage per scope, provider, and model for requests that recorded them.</summary>
    public IReadOnlyList<SessionCacheBreakdown> CacheBreakdown { get; init; } = [];

    public decimal? CacheReadRatePercent => CacheUsage?.GetReadRatePercent();
}
