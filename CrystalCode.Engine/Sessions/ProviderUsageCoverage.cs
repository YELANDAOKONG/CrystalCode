namespace CrystalCode.Engine.Sessions;

/// <summary>Number of requests that reported each token metric.</summary>
public sealed record ProviderUsageCoverage(
    int InputTokens = 0,
    int OutputTokens = 0,
    int ReasoningTokens = 0,
    int CacheReadTokens = 0,
    int CacheWriteTokens = 0,
    int UncachedInputTokens = 0);
