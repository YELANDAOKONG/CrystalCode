using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class RecordingSessionObserver : ISessionObserver
{
    private readonly object _gate = new();
    private readonly List<SessionEvent> _events = [];
    private Action<SessionEvent>? _hook;

    /// <summary>
    /// Runs after each event is recorded, on the publishing thread. A test
    /// uses it to hold the engine at one event.
    /// </summary>
    public Action<SessionEvent>? Hook
    {
        get => Volatile.Read(ref _hook);
        set => Volatile.Write(ref _hook, value);
    }

    public IReadOnlyList<SessionEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public void OnEvent(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            _events.Add(sessionEvent);
        }

        Hook?.Invoke(sessionEvent);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }
}
