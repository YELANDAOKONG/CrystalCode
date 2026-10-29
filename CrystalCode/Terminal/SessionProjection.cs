using CrystalCode.Display.Cards;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Configuration;
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
    private readonly CrystalHome? _home;
    private string? _theme;
    private readonly object _projectionGate = new();
    private readonly List<SessionEvent> _parentEvents = [];
    private readonly Dictionary<string, ChildSessionDocument> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SessionEvent>> _childEvents = new(StringComparer.Ordinal);
    private string? _childFocus;
    private SessionChrome? _parentChrome;

    public SessionProjection(SessionRenderer renderer, CrystalHome? home = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _home = home;
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
                _renderer.SetPending(PendingSettingsText.Summary(started.Chrome.Pending));
                _renderer.SetIdentity(started.Chrome.Provider, started.Chrome.SessionId);
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
                _renderer.SetPending(PendingSettingsText.Summary(changed.Chrome.Pending));
                _renderer.SetIdentity(changed.Chrome.Provider, changed.Chrome.SessionId);
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
                    ToolListWidget.Create(tools.Report),
                    ToolListText.Format(tools.Report));
                break;
            case TodosListed listed:
                _renderer.WriteNote(
                    TodoListWidget.Create(listed.Items),
                    TodoList.Format(listed.Items));
                break;
            case DocumentWritten written:
                _renderer.WriteNote(
                    PluginDocumentWidget.Create(written.Document),
                    written.Document.ToText());
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
            case LoadingProgressed loading:
                if (!_renderer.TryAddStartupItem(ProgressText.Loading(loading.Kind, loading.Name)))
                {
                    _renderer.SetProgress(ProgressText.LoadingItem(loading.Kind, loading.Name));
                }

                break;
            case ApprovalPassed passed:
                if (!replay)
                {
                    _renderer.SetProgress(ProgressText.Running(passed.Call.Name));
                }
                _renderer.WriteApprovalPass(
                    ApprovalCard.PassWidget(
                        passed.Call,
                        passed.Classification,
                        passed.Reason,
                        passed.Review,
                        passed.ExtendedReview),
                    passed.Reason.IsReview);
                break;
            case ApprovalReviewing reviewing:
                _renderer.SetProgress(reviewing.Stage == ApprovalReviewStage.Extended
                    ? ProgressText.ExtendedReviewing
                    : ProgressText.Reviewing);
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
        _renderer.SetTodoBar(preferences.TodoBar, preferences.TodoBarRows);
        _renderer.VerboseTools = preferences.VerboseTools;
        _renderer.VerboseCommands = preferences.VerboseCommands;
        _renderer.VerboseResults = preferences.VerboseResults;
        _renderer.VerboseCalls = preferences.VerboseCalls;
        _renderer.VerboseApprovals = preferences.VerboseApprovals;
        _renderer.VerboseReview = preferences.VerboseReview;
        _renderer.VerboseThinking = preferences.VerboseThinking;
        _renderer.SetStatusLine(
            preferences.StatusLineEnabled,
            preferences.StatusLineFields,
            [.. preferences.StatusLineRules.Select(rule => new StatusFieldRule(rule.Field, rule.Keep, rule.HideBelow))],
            preferences.StatusLineDrop);
        _renderer.SetApprovalColors(preferences.ApprovalColors);
        ApplyTheme(preferences.Theme);
    }

    private void ApplyTheme(string name)
    {
        if (_home is null || name == _theme)
        {
            return;
        }

        if (Theme.TryUseBuiltIn(name))
        {
            _theme = name;
            _renderer.RepaintTheme();
            return;
        }

        if (!ThemeCatalog.TryRead(_home, name, out var colors, out var error)
            || !ThemeCatalog.TryReadGauges(_home, name, out var gauges, out error)
            || !Theme.TryApply(colors, ToGauges(gauges), out error))
        {
            _renderer.WriteError("Theme not applied  " + error);
            return;
        }

        _theme = name;
        _renderer.RepaintTheme();
    }

    private static Dictionary<string, IReadOnlyList<GaugeRule>> ToGauges(
        IReadOnlyDictionary<string, IReadOnlyList<ThemeGaugeRule>> gauges) =>
        gauges.ToDictionary(
            static gauge => gauge.Key,
            static gauge => (IReadOnlyList<GaugeRule>)
                [.. gauge.Value.Select(static rule =>
                    new GaugeRule(rule.From, rule.To, rule.Match, rule.Pattern, rule.Color))],
            StringComparer.OrdinalIgnoreCase);

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
            if (sessionEvent is ActivityChanged changedActivity)
            {
                // Submission is held in every view, including a child view.
                _renderer.HoldSubmission = changedActivity.Activity
                    is SessionActivity.LoadingTools or SessionActivity.LoadingPlugins;
            }

            switch (sessionEvent)
            {
                case ChildSessionChanged changed:
                    var updated = changed.Child;
                    if (ChildSessionFold.IsActive(updated)
                        && _childEvents.TryGetValue(updated.Id, out var liveEvents))
                    {
                        foreach (var value in liveEvents)
                        {
                            updated = ChildSessionFold.Apply(updated, value);
                        }
                    }
                    _children[updated.Id] = updated;
                    if (!ChildSessionFold.IsActive(changed.Child))
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
                        _children[child.ChildSessionId] = ChildSessionFold.Apply(snapshot, child.Event);
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
                case LoadingProgressed:
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
        _renderer.SetProgress(ChildSessionFold.IsActive(child) ? child.Status : string.Empty);
        _renderer.SetTodos(
            [
                .. ChildSessionFold.Todos(child).Select(static todo => new TodoBarItem(
                    TodoList.StatusMark(todo.Status),
                    todo.Content))
            ]);
        foreach (var note in child.Notes)
        {
            _renderer.WriteNote(note);
        }
        Project(new UsageChanged(ChildSessionFold.Usage(child.Usage), ChildSessionFold.Usage(child.CumulativeUsage), model.Context ?? 0, false));
        if (_childEvents.TryGetValue(id, out var events))
        {
            foreach (var item in events)
            {
                Project(item, replay: true);
            }
        }
    }
}
