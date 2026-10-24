using Crystal;

using CrystalCode.Display.Cards;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Terminal.Approvals;

namespace CrystalCode.Terminal;

/// <summary>
/// Projects session events onto the terminal shell.
/// </summary>
internal sealed class SessionProjection : ISessionObserver
{
    private readonly SessionRenderer _renderer;
    private readonly object _projectionGate = new();
    private readonly List<SessionEvent> _parentEvents = [];
    private readonly Dictionary<string, ChildSessionDocument> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SessionEvent>> _childEvents = new(StringComparer.Ordinal);
    private string? _childFocus;
    private SessionChrome? _parentChrome;

    public SessionProjection(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    private void Project(SessionEvent sessionEvent, bool replay = false)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        switch (sessionEvent)
        {
            case SessionStarted started:
                _renderer.WriteHeader(
                    started.Chrome.Model,
                    started.Chrome.WorkspaceRoot,
                    started.Chrome.PlanMode,
                    started.Chrome.Approval,
                    started.Chrome.Thinking,
                    started.Chrome.PromptSet,
                    started.Chrome.ContextWindow,
                    started.Chrome.ServiceTier,
                    started.Chrome.Speed,
                    started.Chrome.Mode);
                _renderer.ChildSessionsEnabled = started.Chrome.AgentsEnabled;
                break;
            case ChromeChanged changed:
                _renderer.SetChrome(
                    changed.Chrome.PlanMode,
                    changed.Chrome.Approval,
                    changed.Chrome.Thinking,
                    changed.Chrome.Model,
                    changed.Chrome.WorkspaceRoot,
                    changed.Chrome.PromptSet,
                    changed.Chrome.ContextWindow,
                    changed.Chrome.ServiceTier,
                    changed.Chrome.Speed,
                    changed.Chrome.Mode);
                _renderer.ChildSessionsEnabled = changed.Chrome.AgentsEnabled;
                break;
            case PreferencesChanged preferences:
                ApplyPreferences(preferences.Preferences);
                break;
            case StatusFieldsChanged status:
                _renderer.SetPluginStatus(status.Fields);
                break;
            case SlashCommandsChanged commands:
                _renderer.SetSlashCommands(SlashOptionMap.From(commands.Commands));
                break;
            case PromptHistoryLoaded history:
                _renderer.SeedPromptHistory(history.Entries);
                break;
            case NoteWritten note when TryCompactionSummary(note.Text, out var summary):
                _renderer.WriteSummary(summary);
                break;
            case NoteWritten note:
                _renderer.WriteNote(note.Text);
                break;
            case ErrorWritten error:
                _renderer.WriteError(error.Text);
                break;
            case UserMessageSent user:
                _renderer.WriteUser(user.Text);
                break;
            case UserMessageRecalled recalled:
                _renderer.RestoreRecalledMessage(recalled.Text, restoreComposer: !replay);
                break;
            case ConversationCleared:
                _renderer.ClearConversation();
                break;
            case HistoryReplayed replayed:
                _renderer.WriteHistory(replayed.Items);
                break;
            case ImageHistoryInvalidated:
                _renderer.ForgetImageHistory();
                break;
            case HelpRequested help:
                _renderer.WriteHelp(help.PluginCommands);
                break;
            case ToolsListed tools:
                _renderer.WriteNote(
                    ToolListWidget.Create(tools.Plan, tools.Work, tools.External, tools.Settings),
                    ToolListText.Format(tools.Plan, tools.Work, tools.External, tools.Settings));
                break;
            case TodosListed listed:
                _renderer.WriteNote(
                    TodoListWidget.Create(listed.Items),
                    TodoList.Format(listed.Items));
                break;
            case StatusReported status:
                _renderer.WriteStatus(status.Status, status.Full);
                break;
            case StatsReported stats:
                _renderer.ShowStatsPage(StatsPageWidget.Create(stats.Report, stats.AllWorkspaces));
                break;
            case SideQuestionSnapshot side:
                _renderer.ShowSideQuestion(side);
                break;
            case ContextEstimated estimated:
                _renderer.ShowContextEstimate(estimated.Tokens);
                break;
            case UsageChanged usage:
                _renderer.ContextWindow = usage.ContextWindow;
                if (usage.Interim)
                {
                    _renderer.ShowLiveUsage(usage.Usage, usage.CumulativeUsage);
                }
                else
                {
                    _renderer.ShowUsage(usage.Usage, usage.CumulativeUsage);
                }

                break;
            case ActivityChanged activity:
                _renderer.SetProgress(Caption(activity.Activity));
                break;
            case ApprovalPassed passed:
                if (!replay)
                {
                    _renderer.SetProgress(ProgressText.Running(passed.Call.Name));
                }
                _renderer.WriteApprovalPass(
                    ApprovalCard.PassWidget(passed.Call, passed.Classification, passed.Reason, passed.Review),
                    passed.Reason == ApprovalPassReason.Review);
                break;
            case ApprovalReviewing:
                _renderer.SetProgress(ProgressText.Reviewing);
                break;
            case QueueChanged queue:
                _renderer.SetQueue(queue.Items);
                break;
            case TodosChanged todos:
                _renderer.SetTodos(
                    [
                        .. todos.Items.Select(static todo => new TodoBarItem(
                            TodoList.StatusMark(todo.Status),
                            todo.Content))
                    ]);
                break;
            case TurnStarted:
                _renderer.BeginTurn();
                break;
            case StreamReceived stream:
                _renderer.OnStreamEvent(stream.StreamEvent);
                break;
            case RetryScheduled retry:
                _renderer.OnRetry(retry.Attempt);
                break;
            case ModelRoundClosed:
                _renderer.OnModelRoundClosed();
                break;
            case ToolCallsIssued calls:
                _renderer.OnToolCalls(calls.Calls);
                break;
            case ToolResultsReceived results:
                _renderer.OnToolResults(results.Results);
                break;
            case TurnFinished finished:
                _renderer.WriteTurnFooter(
                    finished.Result,
                    finished.Usage,
                    finished.CumulativeUsage,
                    finished.ContextWindow);
                break;
            default:
                break;
        }
    }

    private void ApplyPreferences(SessionPreferences preferences)
    {
        _renderer.ShowEstimatedTokens = preferences.EstimatedTokens;
        _renderer.VerboseTools = preferences.VerboseTools;
        _renderer.VerboseCommands = preferences.VerboseCommands;
        _renderer.VerboseResults = preferences.VerboseResults;
        _renderer.VerboseCalls = preferences.VerboseCalls;
        _renderer.VerboseApprovals = preferences.VerboseApprovals;
        _renderer.VerboseReview = preferences.VerboseReview;
        _renderer.VerboseThinking = preferences.VerboseThinking;
        _renderer.SetStatusLine(preferences.StatusLineEnabled, preferences.StatusLineFields);
    }

    private static bool TryCompactionSummary(string text, out string body)
    {
        if (!text.StartsWith(CompactionPrompt.Marker, StringComparison.Ordinal))
        {
            body = string.Empty;
            return false;
        }

        body = text[CompactionPrompt.Marker.Length..].TrimStart('\r', '\n').Trim();
        return body.Length > 0;
    }

    private static string Caption(SessionActivity activity) =>
        activity switch
        {
            SessionActivity.LoadingTools => ProgressText.LoadingTools,
            SessionActivity.LoadingPlugins => ProgressText.LoadingPlugins,
            SessionActivity.Compacting => ProgressText.Compacting,
            SessionActivity.WaitingForModel => ProgressText.WaitingForModel,
            _ => string.Empty
        };

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
