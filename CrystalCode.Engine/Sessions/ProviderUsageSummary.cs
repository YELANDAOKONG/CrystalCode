namespace CrystalCode.Engine.Sessions;

/// <summary>Provider-reported request usage for one model and role in the current turn.</summary>
public sealed record ProviderUsageSummary(
    string Scope,
    string Provider,
    string Model,
    int HttpAttempts,
    int UsageReports,
    long? InputTokens,
    long? OutputTokens,
    long? ReasoningTokens,
    long? CacheReadTokens,
    long? CacheWriteTokens)
{
    public int RequestCount { get; init; }

    public long? UncachedInputTokens { get; init; }

    public ProviderUsageCoverage Coverage { get; init; } = new();

    public IReadOnlyList<ProviderCacheWriteUsage> CacheWritesByLifetime { get; init; } = [];
}
