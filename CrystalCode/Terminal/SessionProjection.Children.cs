using Crystal;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

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
                    var updated = changed.Child;
                    if (updated.Status is "running" or "queued"
                        && _childEvents.TryGetValue(updated.Id, out var liveEvents))
                    {
                        foreach (var value in liveEvents)
                        {
                            updated = UpdateChildSnapshot(updated, value);
                        }
                    }
                    _children[updated.Id] = updated;
                    if (changed.Child.Status is not ("running" or "queued"))
                    {
                        if (_childEvents.TryGetValue(changed.Child.Id, out var completedEvents))
                        {
                            completedEvents.RemoveAll(item => item is not ApprovalPassed);
                        }
                    }
                    _renderer.SetChildSessions(_children.Values.ToArray(), _childFocus);
                    if (_childFocus == changed.Child.Id)
                    {
                        ShowChild(changed.Child.Id);
                    }
                    return;
                case ChildEventReceived child:
                    if (_children.TryGetValue(child.ChildSessionId, out var snapshot)
                        && child.Event is UsageChanged or TodosChanged)
                    {
                        _children[child.ChildSessionId] = UpdateChildSnapshot(snapshot, child.Event);
                        _renderer.SetChildSessions(_children.Values.ToArray(), _childFocus, repaint: false);
                    }
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
                case StatsReported:
                case SideQuestionSnapshot:
                case PreferencesChanged:
                case SlashCommandsChanged:
                case QueueChanged:
                case PromptHistoryLoaded:
                case ImageHistoryInvalidated:
                case StatusFieldsChanged:
                    // Shared surface state is applied once; replay must not restore old input or reopen panels.
                    Project(sessionEvent);
                    return;
                case SessionStarted started:
                    _parentChrome = started.Chrome;
                    _renderer.ChildSessionsEnabled = started.Chrome.AgentsEnabled;
                    break;
                case ChromeChanged chrome:
                    _parentChrome = chrome.Chrome;
                    _renderer.ChildSessionsEnabled = chrome.Chrome.AgentsEnabled;
                    if (!chrome.Chrome.AgentsEnabled)
                    {
                        _children.Clear();
                        _childEvents.Clear();
                        _renderer.SetChildSessions([], null);
                    }
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
            if (!_renderer.ChildSessionsEnabled && _childFocus is not null)
            {
                SwitchView(null);
                return;
            }
            if (_childFocus is null)
            {
                Project(sessionEvent);
            }
        }
    }

    internal bool ReturnToParent()
    {
        lock (_projectionGate)
        {
            if (_childFocus is null || _renderer.OverlayVisible)
            {
                return false;
            }
            SwitchView(null);
            return true;
        }
    }

    internal void DisableAgents()
    {
        lock (_projectionGate)
        {
            _childFocus = null;
            _children.Clear();
            _childEvents.Clear();
            _renderer.SetChildSessions([], null);
            SwitchView(null);
        }
    }

    private void SwitchView(string? id)
    {
        _childFocus = id;
        _renderer.SetChildSessions(_children.Values.ToArray(), id);
        _renderer.ResetView();
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
            Project(item, replay: true);
        }
    }

    private void ShowChild(string id)
    {
        if (!_children.TryGetValue(id, out var child) || _parentChrome is null)
        {
            return;
        }
        _renderer.ResetView();
        _renderer.WriteHistory(TranscriptCodec.Read(child.Archive.ToList()));
        var model = child.Model;
        Project(new ChromeChanged(_parentChrome with
        {
            Model = model.Model ?? "Unknown", Thinking = model.Thinking ?? "default", ContextWindow = model.Context ?? 0,
            ServiceTier = model.Tier ?? string.Empty, Speed = model.Speed ?? string.Empty
        }));
        _renderer.SetProgress(child.Status is "running" or "queued" ? child.Status : string.Empty);
        _renderer.SetTodos(child.Todos.Select(todo => new CrystalCode.Display.Cards.TodoBarItem(
            todo.Status switch
            {
                "in_progress" => TodoList.StatusMark(TodoStatus.InProgress),
                "completed" => TodoList.StatusMark(TodoStatus.Completed),
                "cancelled" => TodoList.StatusMark(TodoStatus.Cancelled),
                _ => TodoList.StatusMark(TodoStatus.Pending)
            }, todo.Content ?? string.Empty)).ToArray());
        foreach (var note in child.Notes)
        {
            _renderer.WriteNote(note);
        }
        Project(new UsageChanged(Usage(child.Usage), Usage(child.CumulativeUsage), model.Context ?? 0, false));
        if (_childEvents.TryGetValue(id, out var events))
        {
            foreach (var item in events)
            {
                Project(item, replay: true);
            }
        }
    }

    private static ChildSessionDocument UpdateChildSnapshot(ChildSessionDocument child, SessionEvent value) => value switch
    {
        UsageChanged usage => child with
        {
            Usage = UsageDocument(usage.Usage),
            CumulativeUsage = UsageDocument(usage.CumulativeUsage)
        },
        TodosChanged todos => child with
        {
            Todos = todos.Items.Select(todo => new SessionTodoDocument
            {
                Id = todo.Id,
                Content = todo.Content,
                Status = todo.Status switch
                {
                    TodoStatus.InProgress => "in_progress",
                    TodoStatus.Completed => "completed",
                    TodoStatus.Cancelled => "cancelled",
                    _ => "pending"
                }
            }).ToArray()
        },
        _ => child
    };

    private static SessionUsageDocument? UsageDocument(TokenUsage? usage) => usage is null ? null
        : new SessionUsageDocument
        {
            InputTokenCount = usage.InputTokenCount,
            OutputTokenCount = usage.OutputTokenCount,
            ReasoningTokenCount = usage.ReasoningTokenCount
        };

    private static TokenUsage? Usage(SessionUsageDocument? usage) => usage is null ? null
        : new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount, usage.ReasoningTokenCount);
}
