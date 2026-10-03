using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class RecordingSessionObserver : ISessionObserver
{
    private readonly object _gate = new();
    private readonly List<SessionEvent> _events = [];

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
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }
}
