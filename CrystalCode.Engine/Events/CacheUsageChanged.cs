using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>The active session's cumulative provider-reported prompt cache usage.</summary>
public sealed record CacheUsageChanged(SessionCacheUsage? Usage) : SessionEvent;
