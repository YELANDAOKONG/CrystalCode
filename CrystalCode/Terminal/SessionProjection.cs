using CrystalCode.Display.Cards;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Terminal;

/// <summary>
/// Projects session events onto the terminal shell.
/// </summary>
internal sealed class SessionProjection : ISessionObserver
{
    private readonly SessionRenderer _renderer;

    public SessionProjection(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public void OnEvent(SessionEvent sessionEvent)
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
                    started.Chrome.PromptSet);
                break;
            case ChromeChanged changed:
                _renderer.SetChrome(
                    changed.Chrome.PlanMode,
                    changed.Chrome.Approval,
                    changed.Chrome.Thinking,
                    changed.Chrome.Model,
                    changed.Chrome.WorkspaceRoot,
                    changed.Chrome.PromptSet);
                break;
            case PreferencesChanged preferences:
                ApplyPreferences(preferences.Preferences);
                break;
            case SlashCommandsChanged commands:
                _renderer.SetSlashCommands(SlashOptionMap.From(commands.Commands));
                break;
            case PromptHistoryLoaded history:
                _renderer.SeedPromptHistory(history.Entries);
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
            case StatusReported status:
                _renderer.WriteStatus(status.Status, status.Full);
                break;
            case StatsReported stats:
                _renderer.ShowStatsPage(StatsPageWidget.Create(stats.Report, stats.AllWorkspaces));
                break;
            case SideQuestionSnapshot side:
                _renderer.ShowSideQuestion(side);
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
        _renderer.VerboseApprovals = preferences.VerboseApprovals;
        _renderer.SetStatusLine(preferences.StatusLineEnabled, preferences.StatusLineFields);
    }

    private static string Caption(SessionActivity activity) =>
        activity switch
        {
            SessionActivity.LoadingTools => ProgressText.LoadingTools,
            SessionActivity.Compacting => ProgressText.Compacting,
            SessionActivity.WaitingForModel => ProgressText.WaitingForModel,
            _ => string.Empty
        };
}
