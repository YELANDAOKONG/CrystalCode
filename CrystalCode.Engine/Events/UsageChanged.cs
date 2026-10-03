using Crystal;

namespace CrystalCode.Engine.Events;

/// <summary>
/// Context and cumulative token usage changed.
/// Interim is true for in-turn updates that may be painted at a throttled rate.
/// </summary>
public sealed record UsageChanged(
    TokenUsage? Usage,
    TokenUsage? CumulativeUsage,
    int ContextWindow,
    bool Interim) : SessionEvent;
