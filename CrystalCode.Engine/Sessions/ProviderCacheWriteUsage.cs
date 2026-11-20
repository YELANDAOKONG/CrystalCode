namespace CrystalCode.Engine.Sessions;

/// <summary>Reported cache writes and request coverage for one lifetime.</summary>
public sealed record ProviderCacheWriteUsage(int LifetimeSeconds, long Tokens, int Reports);
