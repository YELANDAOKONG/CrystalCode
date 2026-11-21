namespace CrystalCode.Engine.Sessions;

/// <summary>Cache usage from one request, dated when the request starts.</summary>
public sealed record SessionCacheUsageSample(
    DateTimeOffset StartedUtc,
    long? InputTokens,
    long? ReadTokens,
    long? WriteTokens,
    IReadOnlyList<ProviderCacheWriteUsage>? WritesByLifetime,
    long? UncachedInputTokens = null,
    string? Scope = null);
