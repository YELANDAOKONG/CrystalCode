using System.Collections.Immutable;

namespace CrystalCode.Plugins.Clients;

/// <summary>Usage reported for a provider request, independent of context accounting.</summary>
public sealed record ProviderRequestUsage(
    int HttpAttempts,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? ReasoningTokens = null,
    long? CacheReadTokens = null,
    long? CacheWriteTokens = null)
{
    public long? UncachedInputTokens { get; init; }

    /// <summary>Reported cache writes by lifetime in seconds.</summary>
    public ImmutableDictionary<int, long>? CacheWritesByLifetime { get; init; }
}
