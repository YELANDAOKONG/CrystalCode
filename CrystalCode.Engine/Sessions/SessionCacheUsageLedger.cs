using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Sessions;

/// <summary>Accumulates provider cache usage across turns of one session.</summary>
internal sealed class SessionCacheUsageLedger(SessionCacheUsage? initial)
{
    private readonly object _gate = new();
    private SessionCacheUsage? _usage = initial;

    public void Record(ProviderRequestUsage usage)
    {
        lock (_gate)
        {
            _usage = (_usage ?? new SessionCacheUsage()).Add(usage);
        }
    }

    public SessionCacheUsage? Snapshot()
    {
        lock (_gate)
        {
            return _usage;
        }
    }
}
