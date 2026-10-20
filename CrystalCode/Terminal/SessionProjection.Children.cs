using Crystal;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Terminal;

internal sealed partial class SessionProjection
{
    private readonly object _projectionGate = new();
    private readonly List<SessionEvent> _parentEvents = [];
    private readonly Dictionary<string, ChildSessionDocument> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SessionEvent>> _childEvents = new(StringComparer.Ordinal);
    private string? _childFocus;
    private SessionChrome? _parentChrome;

    public void OnEvent(SessionEvent sessionEvent)
    {
        lock (_projectionGate)
        {
            switch (sessionEvent)
            {
                case ChildSessionChanged changed:
                    _children[changed.Child.Id] = changed.Child;
                    _renderer.SetChildSessions(_children.Values.ToArray(), _childFocus);
                    if (changed.Child.Status is not ("running" or "queued"))
                    {
                        _childEvents.Remove(changed.Child.Id);
                    }
                    if (_childFocus == changed.Child.Id)
                    {
                        ShowChild(changed.Child.Id);
                    }
                    return;
                case ChildEventReceived child:
                    if (!_childEvents.TryGetValue(child.ChildSessionId, out var events))
                    {
                        events = [];
                        _childEvents[child.ChildSessionId] = events;
                    }
                    events.Add(child.Event);
                    if (_childFocus == child.ChildSessionId)
                    {
                        Project(child.Event);
                    }
                    return;
                case ChildViewRequested view:
                    SwitchView(view.ChildSessionId);
                    return;
                case SessionStarted started:
                    _parentChrome = started.Chrome;
                    break;
                case ChromeChanged chrome:
                    _parentChrome = chrome.Chrome;
                    break;
                case UserMessageSent when _childFocus is not null:
                    SwitchView(null);
                    break;
                case ConversationCleared:
                    _childFocus = null;
                    _renderer.ChildViewActive = false;
                    _children.Clear();
                    _renderer.SetChildSessions([], null);
                    _childEvents.Clear();
                    _parentEvents.Clear();
                    break;
                case HistoryReplayed:
                    _childFocus = null;
                    _renderer.ChildViewActive = false;
                    _children.Clear();
                    _childEvents.Clear();
                    _renderer.SetChildSessions([], null);
                    _parentEvents.Clear();
                    break;
                default:
                    break;
            }
            _parentEvents.Add(sessionEvent);
            if (_childFocus is null || sessionEvent is PreferencesChanged or SlashCommandsChanged or QueueChanged or PromptHistoryLoaded)
            {
                Project(sessionEvent);
            }
        }
    }

    internal bool ReturnToParent()
    {
        lock (_projectionGate)
        {
            if (_childFocus is null)
            {
                return false;
            }
            SwitchView(null);
            return true;
        }
    }

    private void SwitchView(string? id)
    {
        _childFocus = id;
        _renderer.SetChildSessions(_children.Values.ToArray(), id);
        _renderer.ClearConversation();
        if (id is not null)
        {
            ShowChild(id);
            return;
        }
        if (_parentChrome is not null)
        {
            Project(new ChromeChanged(_parentChrome));
        }
        foreach (var item in _parentEvents)
        {
            Project(item);
        }
    }

    private void ShowChild(string id)
    {
        if (!_children.TryGetValue(id, out var child) || _parentChrome is null)
        {
            return;
        }
        _renderer.WriteHistory(TranscriptCodec.Read(child.Archive.ToList()));
        var model = child.Model;
        Project(new ChromeChanged(_parentChrome with
        {
            Model = model.Model ?? "Unknown", Thinking = model.Thinking ?? "default", ContextWindow = model.Context ?? 0,
            ServiceTier = model.Tier ?? string.Empty, Speed = model.Speed ?? string.Empty
        }));
        _renderer.SetProgress(child.Status is "running" or "queued" ? child.Status : string.Empty);
        _renderer.WriteNote($"Child · {child.Agent} · {child.Label} · {child.Id[..Math.Min(8, child.Id.Length)]}  /child parent returns");
        foreach (var note in child.Notes)
        {
            _renderer.WriteNote(note);
        }
        if (_childEvents.TryGetValue(id, out var events))
        {
            foreach (var item in events)
            {
                Project(item);
            }
        }
        Project(new UsageChanged(Usage(child.Usage), Usage(child.CumulativeUsage), model.Context ?? 0, false));
    }

    private static TokenUsage? Usage(SessionUsageDocument? usage) => usage is null ? null
        : new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount, usage.ReasoningTokenCount);
}
