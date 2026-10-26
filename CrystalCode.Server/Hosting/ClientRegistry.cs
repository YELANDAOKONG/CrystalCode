using System.Collections.Concurrent;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// Tracks which clients hold an open event stream, so questions raised by a
/// client's command go to that client while it is connected.
/// </summary>
public sealed class ClientRegistry
{
    private readonly ConcurrentDictionary<string, int> _streams = new(StringComparer.Ordinal);

    public event Action<string>? Disconnected;

    public bool IsConnected(string client) => _streams.TryGetValue(client, out var count) && count > 0;

    public IDisposable Connect(string client)
    {
        _streams.AddOrUpdate(client, 1, static (_, count) => count + 1);
        return new Connection(this, client);
    }

    private void Release(string client)
    {
        var remaining = _streams.AddOrUpdate(client, 0, static (_, count) => Math.Max(0, count - 1));
        if (remaining == 0)
        {
            _streams.TryRemove(new KeyValuePair<string, int>(client, 0));
            Disconnected?.Invoke(client);
        }
    }

    private sealed class Connection(ClientRegistry registry, string client) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                registry.Release(client);
            }
        }
    }
}
