namespace CrystalCode.Engine.Sessions;

internal sealed record SessionStatsReport(
    int Sessions,
    int UserTurns,
    int ModelCalls,
    int ToolCalls,
    long InputTokens,
    long OutputTokens,
    long ReasoningTokens,
    long TotalTokens,
    long AverageTokensPerSession,
    long MedianTokensPerSession,
    int WindowDays,
    DateTimeOffset? Earliest,
    DateTimeOffset? Latest,
    IReadOnlyList<SessionToolCount> TopTools);
