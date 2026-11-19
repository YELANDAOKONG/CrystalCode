namespace CrystalCode.Plugins.Clients;

/// <summary>Usage reported for a provider request, independent of context accounting.</summary>
public sealed record ProviderRequestUsage(
    int HttpAttempts,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? ReasoningTokens = null,
    long? CacheReadTokens = null,
    long? CacheWriteTokens = null);
