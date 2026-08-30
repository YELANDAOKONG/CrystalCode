using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Compaction;
using CrystalHarness.Configuration;
using CrystalHarness.Display;
using CrystalHarness.Home;
using CrystalHarness.Plugins;
using CrystalHarness.Prompts;
using CrystalHarness.Tools;

namespace CrystalHarness.Sessions;

/// <summary>
/// Interactive Plan/Work loop. Owns chrome, catalogs, approval, and turns.
/// </summary>
public sealed class CodingSession
{
    private readonly IStreamingChatClient _client;
    private readonly HarnessSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly PromptStore _promptStore;
    private readonly SessionStore _sessionStore;
    private readonly ContextCompactor _compactor;
    private readonly PluginRegistry _plugins;
    private readonly SessionRenderer _renderer;
    private readonly SessionReviewContext _reviewContext = new();
    private readonly SessionLedger _ledger = new();
    private readonly TodoList _todos = new();
    private readonly MessageQueue _queue = new();
    private readonly GrantStore _grants;
    private Workspace _workspace;
    private PromptSet _prompts;
    private ApprovalMode _approval;
    private bool _planMode;
    private List<ChatItem> _transcript;
    private string _sessionId;
    private DateTimeOffset _sessionCreatedUtc;
    private IToolExecutor _workExecutor = null!;
    private IToolExecutor _planExecutor = null!;
    private Task<TurnResult>? _turnTask;
    private CancellationTokenSource? _turnSource;
    private bool _turnActive;
    private int _idleCancels;

    private CodingSession(
        IStreamingChatClient client,
        HarnessSettings settings,
        SettingsStore settingsStore,
        CrystalHome home,
        Workspace workspace,
        SessionRenderer renderer,
        PluginRegistry plugins)
    {
        _client = client;
        _settings = settings;
        _settingsStore = settingsStore;
        _promptStore = new PromptStore(home);
        _sessionStore = new SessionStore(home);
        _compactor = new ContextCompactor(client);
        ArgumentNullException.ThrowIfNull(plugins);
        _workspace = workspace;
        _plugins = plugins;
        _renderer = renderer;
        _approval = settings.Approval;
        _grants = new GrantStore(home);
        _prompts = _promptStore.Load(workspace.Root);
        _transcript = [new ChatMessage(ChatRole.System, _prompts.WorkSystem)];
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
        RebuildExecutors();
    }

    public static CodingSession Create(
        IStreamingChatClient client,
        HarnessSettings settings,
        SettingsStore settingsStore,
        CrystalHome home,
        string workspaceRoot,
        PluginRegistry? plugins = null)
    {
        var renderer = new SessionRenderer();
        return new CodingSession(
            client,
            settings,
            settingsStore,
            home,
            new Workspace(workspaceRoot),
            renderer,
            plugins ?? PluginRegistry.CreateBuiltIn());
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var screen = _renderer.Open();
        _renderer.ContextWindow = _settings.ActiveModel.ContextWindow;
        _renderer.AfterTools = PromoteAfterTools;
        _renderer.SetSlashCommands(_plugins.Commands);
        _renderer.WriteHeader(
            _settings.Model,
            _workspace.Root,
            _planMode,
            _approval);

        using var promptSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            if (_turnActive && _turnSource is not null)
            {
                _turnSource.Cancel();
                return;
            }

            _idleCancels++;
            promptSource.Cancel();
        };

        while (true)
        {
            PromptRead read;
            try
            {
                read = await _renderer.ReadPromptAsync(
                    _planMode,
                    TogglePlanFromPrompt,
                    _turnTask,
                    preserveStream: _turnActive,
                    ignorePause: false,
                    promptSource.Token);
                _idleCancels = 0;
            }
            catch (OperationCanceledException)
            {
                if (_idleCancels >= 2 || cancellationToken.IsCancellationRequested)
                {
                    await FinishTurnAsync();
                    _renderer.WriteNote("bye");
                    return 0;
                }

                if (!promptSource.IsCancellationRequested)
                {
                    continue;
                }

                _renderer.WriteNote("ctrl+c again to exit");
                if (!promptSource.TryReset())
                {
                    await FinishTurnAsync();
                    return 0;
                }

                continue;
            }

            if (read.TurnEnded)
            {
                await FinishTurnAsync();
                StartTurnIfQueued();
                continue;
            }

            var input = read.Text.Trim();
            if (_turnActive)
            {
                if (input.Length > 0)
                {
                    if (StopsBusyTurn(input))
                    {
                        _turnSource?.Cancel();
                        await FinishTurnAsync();
                    }

                    if (TryHandleCommand(input, out var busyExit))
                    {
                        if (busyExit)
                        {
                            return 0;
                        }

                        continue;
                    }

                    Enqueue(input);
                    continue;
                }

                _turnSource?.Cancel();
                await FinishTurnAsync();
                StartTurnIfQueued();
                continue;
            }

            if (input.Length == 0)
            {
                continue;
            }

            if (TryHandleCommand(input, out var exit))
            {
                if (exit)
                {
                    return 0;
                }

                continue;
            }

            StartTurn(input);
        }
    }

    private bool TryHandleCommand(string input, out bool exit)
    {
        exit = false;
        if (!SessionCommand.TryParse(input, out var command))
        {
            return false;
        }

        switch (command.Verb)
        {
            case SessionVerb.Help:
                _renderer.WriteHelp(_plugins.Commands);
                return true;
            case SessionVerb.Plan:
                TogglePlanFromPrompt();
                _renderer.SetChrome(_planMode, _approval);
                _renderer.WriteNote(ModeLabel.For(_planMode));
                return true;
            case SessionVerb.Approval:
                ChangeApproval(command.Argument);
                return true;
            case SessionVerb.Status:
                _renderer.WriteStatus(
                    _ledger,
                    _workspace.Root,
                    _planMode,
                    _approval,
                    _settings.ActiveModel.ContextWindow);
                return true;
            case SessionVerb.Clear:
                BeginNewSession();
                _renderer.ClearConversation();
                _renderer.WriteNote("new conversation");
                return true;
            case SessionVerb.Cd:
                ChangeDirectory(command.Argument);
                return true;
            case SessionVerb.Resume:
                ResumeSession(command.Argument);
                return true;
            case SessionVerb.Quit:
                exit = true;
                return true;
            case SessionVerb.Unknown:
                if (TryExecutePluginCommand(command.Argument))
                {
                    return true;
                }

                _renderer.WriteError("unknown command  " + command.Argument);
                return true;
            default:
                return false;
        }
    }

    private void ChangeApproval(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            _approval = NextApproval(_approval);
        }
        else
        {
            try
            {
                _approval = ApprovalMode.Parse(argument);
            }
            catch (ArgumentException exception)
            {
                _renderer.WriteError(exception.Message);
                return;
            }
        }

        _settingsStore.Save(_settings.WithApproval(_approval));
        RebuildExecutors();
        _renderer.SetChrome(_planMode, _approval);
        _renderer.WriteNote("approval  " + ApprovalLabel.For(_approval));
    }

    private void ChangeDirectory(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            _renderer.WriteNote(_workspace.Root);
            return;
        }

        if (_workspace.TrySetRoot(argument, out var error))
        {
            ReloadPrompts();
            RebuildExecutors();
            _renderer.WriteNote("workspace  " + _workspace.Root);
            return;
        }

        _renderer.WriteError(error);
    }

    private bool TogglePlanFromPrompt()
    {
        _planMode = !_planMode;
        if (_transcript.Count > 0 && _transcript[0] is ChatMessage { Role.Value: "system" })
        {
            _transcript[0] = new ChatMessage(ChatRole.System, CurrentSystemText());
        }

        return _planMode;
    }

    private string CurrentSystemText() =>
        _planMode ? _prompts.PlanSystem : _prompts.WorkSystem;

    private void ReloadPrompts()
    {
        _prompts = _promptStore.Load(_workspace.Root);
        if (_transcript.Count > 0 && _transcript[0] is ChatMessage { Role.Value: "system" })
        {
            _transcript[0] = new ChatMessage(ChatRole.System, CurrentSystemText());
        }
    }

    private async Task CompactIfNeededAsync(TurnResult result, CancellationToken cancellationToken)
    {
        if (!ContextAccountant.ShouldCompact(
                result.Usage,
                _settings.ActiveModel.ContextWindow,
                _settings.CompactionThreshold))
        {
            return;
        }

        _renderer.WriteNote("compacting context...");
        var outcome = await _compactor.CompactAsync(
            _transcript,
            _todos.Format(),
            cancellationToken);
        if (!outcome.Compacted)
        {
            _renderer.WriteNote("compaction skipped");
            return;
        }

        _transcript = [.. outcome.Transcript];
        _renderer.WriteNote("compacted context");
    }

    private void SaveSession()
    {
        _sessionStore.Save(
            new SessionDocument
            {
                Id = _sessionId,
                Workspace = _workspace.Root,
                PlanMode = _planMode,
                CreatedUtc = _sessionCreatedUtc,
                Items = TranscriptCodec.Write(_transcript),
                Todos = WriteTodos()
            });
    }

    private void BeginNewSession()
    {
        DiscardQueue();
        _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
        _ledger.Clear();
        _todos.Clear();
        _reviewContext.CurrentUserRequest = string.Empty;
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
        SaveSession();
    }

    private void ResumeSession(string argument)
    {
        SessionDocument document;
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (!_sessionStore.TryLoadLatest(_workspace.Root, out document))
            {
                _renderer.WriteError("no session for this workspace");
                return;
            }
        }
        else if (!_sessionStore.TryLoad(argument, out document))
        {
            _renderer.WriteError("session not found  " + argument.Trim());
            return;
        }

        var items = TranscriptCodec.Read(document.Items);
        if (items.Count == 0)
        {
            _renderer.WriteError("session is empty");
            return;
        }

        _sessionId = document.Id!;
        _sessionCreatedUtc = document.CreatedUtc ?? DateTimeOffset.UtcNow;
        _planMode = document.PlanMode;
        _transcript = items;
        if (_transcript[0] is ChatMessage { Role.Value: "system" })
        {
            _transcript[0] = new ChatMessage(ChatRole.System, CurrentSystemText());
        }

        _todos.Clear();
        _todos.Replace(ReadTodos(document.Todos));
        _ledger.Clear();
        DiscardQueue();
        _reviewContext.CurrentUserRequest = string.Empty;
        RebuildExecutors();
        _renderer.WriteNote("resumed  " + _sessionId);
    }

    private List<SessionTodoDocument> WriteTodos()
    {
        var documents = new List<SessionTodoDocument>();
        foreach (var item in _todos.Snapshot())
        {
            documents.Add(
                new SessionTodoDocument
                {
                    Id = item.Id,
                    Content = item.Content,
                    Status = TodoList.StatusName(item.Status)
                });
        }

        return documents;
    }

    private static List<TodoItem> ReadTodos(IEnumerable<SessionTodoDocument> documents)
    {
        var items = new List<TodoItem>();
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Id)
                || string.IsNullOrWhiteSpace(document.Content)
                || !TodoList.TryParseStatus(document.Status, out var status))
            {
                continue;
            }

            items.Add(new TodoItem(document.Id, document.Content, status));
        }

        return items;
    }

    private void RebuildExecutors()
    {
        var renderer = _renderer;
        var approvalPrompt = new ApprovalPrompt(renderer);
        var question = new QuestionPrompt(renderer);
        var reviewer = new ModelApprovalReviewer(_client, _prompts.Review);
        var policy = new ApprovalPolicy(
            _approval,
            _workspace,
            _grants,
            approvalPrompt,
            reviewer,
            _reviewContext,
            _plugins.Classifiers);
        var options = new ToolExecutionOptions(ToolExecutionMode.Serial, 1);
        _workExecutor = new ToolExecutor(
            WorkspaceCatalog.CreateWork(_workspace, _todos, question, _plugins),
            options,
            policy.DecideAsync,
            HarnessExceptionMapper.MapAsync);
        _planExecutor = new ToolExecutor(
            WorkspaceCatalog.CreatePlan(_workspace, _todos, question, _plugins),
            options,
            exceptionMapper: HarnessExceptionMapper.MapAsync);
    }

    private bool TryExecutePluginCommand(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return false;
        }

        var body = trimmed[1..];
        var space = body.IndexOf(' ');
        var name = space < 0 ? body : body[..space];
        var argument = space < 0 ? string.Empty : body[(space + 1)..].Trim();
        var command = _plugins.FindCommand(name);
        if (command is null)
        {
            return false;
        }

        command.Execute(argument, _renderer);
        return true;
    }

    private static ApprovalMode NextApproval(ApprovalMode current)
    {
        if (current == ApprovalMode.Default)
        {
            return ApprovalMode.AutoEdit;
        }

        if (current == ApprovalMode.AutoEdit)
        {
            return ApprovalMode.Review;
        }

        if (current == ApprovalMode.Review)
        {
            return ApprovalMode.Full;
        }

        return ApprovalMode.Default;
    }

    private void PromoteAfterTools()
    {
        if (_queue.Count > 0)
        {
            _turnSource?.Cancel();
        }
    }

    private void ShowQueue()
    {
        _renderer.SetQueue(_queue.Snapshot());
    }

    private void DiscardQueue()
    {
        _queue.Clear();
        ShowQueue();
    }

    private void Enqueue(string input)
    {
        _queue.Enqueue(input);
        ShowQueue();
    }

    private void StartTurnIfQueued()
    {
        var next = _queue.Drain();
        ShowQueue();
        if (next is not null)
        {
            StartTurn(next);
        }
    }

    private void StartTurn(string input)
    {
        _renderer.WriteUser(input);
        _reviewContext.CurrentUserRequest = input;
        _transcript.Add(new ChatMessage(ChatRole.User, input));
        _turnSource = new CancellationTokenSource();
        _turnActive = true;
        _renderer.BeginTurn();
        _turnTask = ExecuteTurnAsync(_turnSource.Token);
    }

    private Task<TurnResult> ExecuteTurnAsync(CancellationToken cancellationToken)
    {
        var turn = new StreamingTurn(
            _client,
            _planMode ? _planExecutor : _workExecutor,
            TurnLimits.CreateDefault(),
            _renderer);
        return turn.RunAsync(_transcript, cancellationToken);
    }

    private async Task FinishTurnAsync()
    {
        if (_turnTask is null)
        {
            return;
        }

        TurnResult? result = null;
        try
        {
            result = await _turnTask;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _renderer.WriteError(exception.Message);
        }
        finally
        {
            _turnActive = false;
            _turnTask = null;
            _turnSource?.Dispose();
            _turnSource = null;
        }

        if (result is null)
        {
            return;
        }

        _transcript = [.. result.Transcript];
        if (result.ModelCallCount > 0)
        {
            _ledger.Record(result);
        }

        if (result.StopReason == TurnStopReason.Completed)
        {
            await CompactIfNeededAsync(result, CancellationToken.None);
        }

        SaveSession();
        _renderer.WriteTurnFooter(
            result,
            _ledger,
            _settings.ActiveModel.ContextWindow);
    }

    private static bool StopsBusyTurn(string input)
    {
        return SessionCommand.TryParse(input, out var command)
            && command.Verb is SessionVerb.Quit or SessionVerb.Clear or SessionVerb.Resume;
    }
}
