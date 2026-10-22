using CrystalCode.Engine.Events;
using CrystalCode.Plugins.Status;
using CrystalCode.Plugins.Status.Interfaces;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Latest text for admitted plugin status fields. Plugins push changes.
/// A retired board, and every sink it opened, ignores later updates.
/// Snapshots leave in the order they were taken, including a clear from
/// <see cref="Retire"/> or <see cref="Close"/>.
/// </summary>
public sealed class PluginStatusBoard
{
    private readonly object _gate = new();
    private readonly Action<IReadOnlyList<StatusFieldValue>> _publish;
    private readonly Action<string> _note;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejected = new(StringComparer.Ordinal);
    private readonly Queue<IReadOnlyList<StatusFieldValue>> _pending = new();
    private readonly List<StatusSink> _sinks = [];
    private bool _retired;
    private bool _publishing;

    public PluginStatusBoard(
        Action<IReadOnlyList<StatusFieldValue>> publish,
        Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(note);
        _publish = publish;
        _note = note;
    }

    public IPluginStatusUpdates Open(string directoryName, IReadOnlyCollection<string> names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentNullException.ThrowIfNull(names);
        var owned = new HashSet<string>(names, StringComparer.Ordinal);
        lock (_gate)
        {
            if (_retired)
            {
                return StatusSink.Closed;
            }

            var sink = new StatusSink(this, directoryName, owned);
            _sinks.Add(sink);
            return sink;
        }
    }

    public void Retire()
    {
        var drain = false;
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            foreach (var sink in _sinks)
            {
                sink.Close();
            }

            if (_values.Count > 0)
            {
                _values.Clear();
                drain = Enqueue([]);
            }
        }

        if (drain)
        {
            Drain();
        }
    }

    public void Close(IPluginStatusUpdates updates)
    {
        if (updates is not StatusSink sink || ReferenceEquals(sink, StatusSink.Closed))
        {
            return;
        }

        var drain = false;
        lock (_gate)
        {
            if (sink.IsClosed)
            {
                return;
            }

            sink.Close();
            var removed = false;
            foreach (var name in sink.Names)
            {
                removed |= _values.Remove(name);
            }

            if (removed)
            {
                drain = Enqueue(Snapshot());
            }
        }

        if (drain)
        {
            Drain();
        }
    }

    private void Apply(StatusSink sink, string? name, string? text)
    {
        if (sink.IsClosed)
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name.Trim().ToLowerInvariant();
        if (!sink.Owns(key))
        {
            Reject(sink.DirectoryName, name);
            return;
        }

        var normalized = PluginStatusText.Normalize(text, PluginStatusText.MaximumTextLength);
        var drain = false;
        lock (_gate)
        {
            if (_retired || sink.IsClosed)
            {
                return;
            }

            if (normalized.Length == 0)
            {
                if (!_values.Remove(key))
                {
                    return;
                }
            }
            else if (_values.TryGetValue(key, out var current)
                && string.Equals(current, normalized, StringComparison.Ordinal))
            {
                return;
            }
            else
            {
                _values[key] = normalized;
            }

            drain = Enqueue(Snapshot());
        }

        if (drain)
        {
            Drain();
        }
    }

    private bool Enqueue(IReadOnlyList<StatusFieldValue> snapshot)
    {
        _pending.Enqueue(snapshot);
        if (_publishing)
        {
            return false;
        }

        _publishing = true;
        return true;
    }

    private void Drain()
    {
        while (true)
        {
            IReadOnlyList<StatusFieldValue> snapshot;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _publishing = false;
                    return;
                }

                snapshot = _pending.Dequeue();
            }

            _publish(snapshot);
        }
    }

    private void Reject(string directoryName, string? name)
    {
        var shown = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        var token = directoryName + "\n" + shown.ToLowerInvariant();
        lock (_gate)
        {
            if (!_rejected.Add(token))
            {
                return;
            }
        }

        var label = shown.Length == 0 ? "(empty)" : shown;
        _note($"Plugin '{directoryName}' ignored status field '{label}'.");
    }

    private StatusFieldValue[] Snapshot()
    {
        var values = new StatusFieldValue[_values.Count];
        var index = 0;
        foreach (var pair in _values)
        {
            values[index] = new StatusFieldValue(pair.Key, pair.Value);
            index++;
        }

        return values;
    }

    private sealed class StatusSink : IPluginStatusUpdates
    {
        private readonly PluginStatusBoard? _board;
        private readonly HashSet<string> _owned;
        private int _closed;

        private StatusSink()
        {
            _owned = new HashSet<string>(StringComparer.Ordinal);
            _closed = 1;
            DirectoryName = string.Empty;
        }

        public StatusSink(PluginStatusBoard board, string directoryName, HashSet<string> owned)
        {
            _board = board;
            DirectoryName = directoryName;
            _owned = owned;
        }

        public static StatusSink Closed { get; } = new();

        public string DirectoryName { get; }

        public IEnumerable<string> Names => _owned;

        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        public bool Owns(string name) => _owned.Contains(name);

        public void Close() => Volatile.Write(ref _closed, 1);

        public void Set(string name, string text)
        {
            if (IsClosed || _board is null)
            {
                return;
            }

            _board.Apply(this, name, text);
        }

        public void Clear(string name) => Set(name, string.Empty);
    }
}
