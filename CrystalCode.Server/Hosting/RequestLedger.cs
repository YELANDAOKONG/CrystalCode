using System.Collections.Concurrent;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// Remembers the result of a state-changing request by client and
/// <c>clientRequestId</c> for ten minutes, so a retried request after a
/// dropped connection returns the first result instead of running again.
/// </summary>
public sealed class RequestLedger
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public RequestLedger(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs <paramref name="work"/> once per key. A repeat while the first is
    /// still running waits for the same result.
    /// </summary>
    public Task<RequestOutcome> RunOnceAsync(string client, string? requestId, Func<Task<RequestOutcome>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return work();
        }

        Prune();
        var key = client + "\n" + requestId.Trim();
        var entry = _entries.GetOrAdd(key, _ => new Entry(new Lazy<Task<RequestOutcome>>(work), _time.GetUtcNow() + Lifetime));
        return entry.Result.Value;
    }

    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var pair in _entries)
        {
            if (pair.Value.Expires <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Entry(Lazy<Task<RequestOutcome>> Result, DateTimeOffset Expires);
}
