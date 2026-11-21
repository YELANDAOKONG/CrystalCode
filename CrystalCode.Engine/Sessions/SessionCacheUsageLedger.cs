using System.Collections.Immutable;

namespace CrystalCode.Engine.Sessions;

/// <summary>Accumulates provider cache usage across turns of one session.</summary>
internal sealed class SessionCacheUsageLedger(
    SessionCacheUsage? initial, ImmutableList<SessionCacheUsageSample>? initialSamples)
{
    private readonly object _gate = new();
    private SessionCacheUsage? _usage = initial;
    private ImmutableList<SessionCacheUsageSample>? _samples = initialSamples;

    public void Record(SessionCacheUsageSample usage)
    {
        lock (_gate)
        {
            _usage = (_usage ?? new SessionCacheUsage()).Add(usage);
            _samples = (_samples ?? ImmutableList<SessionCacheUsageSample>.Empty).Add(usage);
        }
    }

    public (SessionCacheUsage? Usage, ImmutableList<SessionCacheUsageSample>? Samples) Snapshot()
    {
        lock (_gate)
        {
            return (_usage, _samples);
        }
    }
}
