using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Events;

/// <summary>
/// Receives every engine event, gives it a sequence number, serializes it
/// once, keeps it in a ring buffer bounded by count and bytes, applies it to
/// the session state, and wakes subscribers. <see cref="OnEvent"/> never
/// waits on the network, so a slow client cannot stall the engine. A
/// subscriber whose cursor has left the buffer is told to resync.
/// </summary>
public sealed class EventHub : ISessionObserver
{
    private readonly object _gate = new();
    private readonly LinkedList<ServerEvent> _log = new();
    private readonly List<Subscriber> _subscribers = [];
    private readonly SessionState _state = new();
    private readonly int _maximumCount;
    private readonly long _maximumBytes;
    private readonly TimeProvider _time;
    private long _bytes;
    private long _sequence;
    private Func<string?> _sessionId = static () => null;

    public EventHub(int maximumCount, long maximumBytes, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        _maximumCount = maximumCount;
        _maximumBytes = maximumBytes;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Reads the session id each envelope carries. It changes on /clear,
    /// /resume, and /fork, and is null before the session exists.
    /// </summary>
    public void BindSessionId(Func<string?> sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate)
        {
            _sessionId = sessionId;
        }
    }

    public long LastSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    public void OnEvent(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            _state.Apply(sessionEvent);
            var target = WireEvents.IsReply(sessionEvent) ? sessionEvent.Origin : null;
            AppendUnlocked(
                WireEvents.Type(sessionEvent),
                target,
                sessionEvent.Origin,
                sessionEvent is ChildEventReceived child ? child : null,
                writer => WireEvents.Data(writer, sessionEvent));
        }
    }

    /// <summary>Publishes a server event (state, interactions, closing).</summary>
    public void Publish(string type, string? target, Action<Utf8JsonWriter> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            AppendUnlocked(type, target, origin: null, child: null, writer =>
            {
                writer.WriteStartObject("data");
                data(writer);
                writer.WriteEndObject();
            });
        }
    }

    /// <summary>
    /// Writes the snapshot and the last sequence it covers under one lock, so
    /// a client that follows with <c>after=lastSeq</c> neither misses nor
    /// repeats an event.
    /// </summary>
    public void WriteSnapshot(Utf8JsonWriter writer, Action<Utf8JsonWriter, SessionSnapshot> write)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
        {
            writer.WriteNumber("lastSeq", _sequence);
            WireWriter.Optional(writer, "sessionId", _sessionId());
            write(writer, _state.Snapshot());
        }
    }

    /// <summary>The current session state, without the sequence number.</summary>
    public SessionSnapshot Snapshot()
    {
        lock (_gate)
        {
            return _state.Snapshot();
        }
    }

    /// <summary>
    /// Opens a cursor after <paramref name="after"/>. Returns null when that
    /// cursor is older than the buffer, which means the client must fetch a
    /// new snapshot. Events for other clients are skipped.
    /// </summary>
    public Subscription? Subscribe(long after, string client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        lock (_gate)
        {
            if (after > _sequence)
            {
                after = _sequence;
            }

            var oldest = _log.First?.Value.Sequence ?? _sequence + 1;
            if (after < oldest - 1)
            {
                return null;
            }

            var subscriber = new Subscriber(client);
            foreach (var item in _log)
            {
                if (item.Sequence > after && Visible(item, client))
                {
                    subscriber.Channel.Writer.TryWrite(item);
                }
            }

            _subscribers.Add(subscriber);
            return new Subscription(this, subscriber);
        }
    }

    private void AppendUnlocked(
        string type,
        string? target,
        string? origin,
        ChildEventReceived? child,
        Action<Utf8JsonWriter> data)
    {
        var sequence = ++_sequence;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", sequence);
            writer.WriteString("type", type);
            writer.WriteString("time", _time.GetUtcNow());
            WireWriter.Optional(writer, "sessionId", _sessionId());
            if (origin is not null)
            {
                writer.WriteString("origin", origin);
            }

            if (target is not null)
            {
                writer.WriteString("target", target);
            }

            if (child is not null)
            {
                writer.WriteStartObject("source");
                writer.WriteString("childSessionId", child.ChildSessionId);
                writer.WriteString("invocationId", child.InvocationId);
                writer.WriteEndObject();
            }

            data(writer);
            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        var item = new ServerEvent(sequence, target, type, json);
        _log.AddLast(item);
        _bytes += json.Length;
        while (_log.Count > _maximumCount || (_bytes > _maximumBytes && _log.Count > 1))
        {
            _bytes -= _log.First!.Value.Json.Length;
            _log.RemoveFirst();
        }

        foreach (var subscriber in _subscribers)
        {
            if (Visible(item, subscriber.Client))
            {
                subscriber.Channel.Writer.TryWrite(item);
            }
        }
    }

    private static bool Visible(ServerEvent item, string client) =>
        item.Target is null || string.Equals(item.Target, client, StringComparison.Ordinal);

    private void Remove(Subscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }

        subscriber.Channel.Writer.TryComplete();
    }

    /// <summary>Ends every open stream, for shutdown.</summary>
    public void CompleteAll()
    {
        lock (_gate)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Channel.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    internal sealed class Subscriber(string client)
    {
        public string Client { get; } = client;

        public Channel<ServerEvent> Channel { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<ServerEvent>(
                new UnboundedChannelOptions { SingleReader = true });
    }

    /// <summary>An open cursor. Dispose to stop receiving.</summary>
    public sealed class Subscription : IDisposable
    {
        private readonly EventHub _hub;
        private readonly Subscriber _subscriber;

        internal Subscription(EventHub hub, Subscriber subscriber)
        {
            _hub = hub;
            _subscriber = subscriber;
        }

        public ChannelReader<ServerEvent> Reader => _subscriber.Channel.Reader;

        public void Dispose() => _hub.Remove(_subscriber);
    }
}
