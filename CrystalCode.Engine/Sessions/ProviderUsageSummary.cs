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
    long? CacheWriteTokens);
