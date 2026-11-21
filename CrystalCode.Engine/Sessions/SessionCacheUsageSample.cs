namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Cache usage from one request, dated when the request starts. Scope, provider,
/// and model are stored as sent so a view can group them any way it needs.
/// </summary>
public sealed record SessionCacheUsageSample(
    DateTimeOffset StartedUtc,
    long? InputTokens,
    long? ReadTokens,
    long? WriteTokens,
    IReadOnlyList<ProviderCacheWriteUsage>? WritesByLifetime,
    long? UncachedInputTokens = null,
    string? Scope = null,
    string? Provider = null,
    string? Model = null);
