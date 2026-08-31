using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Compaction;
using CrystalHarness.Configuration;
using CrystalHarness.Display.Cards;
using CrystalHarness.Display.Composer;
using CrystalHarness.Display.Paint;
using CrystalHarness.Display.Shell;
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
    private HarnessSettings _settings;
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
    private ThinkingSelection _thinkingEffort;
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
        _thinkingEffort = settings.ThinkingEffort;
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
        try
        {
            return await RunLoopAsync(cancellationToken);
        }
        finally
        {
            WriteResumeHint();
        }
    }

    private async Task<int> RunLoopAsync(CancellationToken cancellationToken)
    {
        using var screen = _renderer.Open();
        _renderer.ContextWindow = _settings.ActiveModel.ContextWindow;
        _renderer.AfterTools = PromoteAfterTools;
        _renderer.SetSlashCommands(
            _plugins.Commands,
            ThinkingCompletions.For(_settings.ActiveModel));
        _renderer.WriteHeader(
            _settings.Model,
            _workspace.Root,
            _planMode,
            _approval,
            CurrentThinkingStatus());

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

            if (_renderer.TryClearComposer())
            {
                _idleCancels = 0;
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

                    var busy = await TryHandleCommandAsync(input, promptSource.Token);
                    if (busy.Handled)
                    {
                        if (busy.Exit)
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

            var command = await TryHandleCommandAsync(input, promptSource.Token);
            if (command.Handled)
            {
                if (command.Exit)
                {
                    return 0;
                }

                continue;
            }

            StartTurn(input);
        }
    }

    private async Task<(bool Handled, bool Exit)> TryHandleCommandAsync(
        string input,
        CancellationToken cancellationToken)
    {
        if (!SessionCommand.TryParse(input, out var parsed))
        {
            return (false, false);
        }

        if (parsed.Verb == SessionVerb.Compact)
        {
            await CompactForcedAsync(cancellationToken);
            return (true, false);
        }

        return HandleCommand(parsed);
    }

    private (bool Handled, bool Exit) HandleCommand(SessionCommand command)
    {
        switch (command.Verb)
        {
            case SessionVerb.Help:
                _renderer.WriteHelp(_plugins.Commands);
                return (true, false);
            case SessionVerb.Plan:
                TogglePlanFromPrompt();
                RefreshChrome();
                _renderer.WriteNote(ModeLabel.For(_planMode));
                return (true, false);
            case SessionVerb.Approval:
                ChangeApproval(command.Argument);
                return (true, false);
            case SessionVerb.Thinking:
                ChangeThinking(command.Argument);
                return (true, false);
            case SessionVerb.Status:
                _renderer.WriteStatus(
                    _ledger,
                    _workspace.Root,
                    _planMode,
                    _approval,
                    _settings.ActiveModel.ContextWindow,
                    CurrentThinkingStatus());
                return (true, false);
            case SessionVerb.Clear:
                BeginNewSession();
                _renderer.ClearConversation();
                _renderer.ShowUsage(null);
                _renderer.WriteNote("new conversation");
                return (true, false);
            case SessionVerb.Cd:
                ChangeDirectory(command.Argument);
                return (true, false);
            case SessionVerb.Resume:
                ResumeSession(command.Argument);
                return (true, false);
            case SessionVerb.Compact:
                return (true, false);
            case SessionVerb.Quit:
                return (true, true);
            case SessionVerb.Unknown:
                if (_plugins.TryExecute(command.Argument, _renderer))
                {
                    return (true, false);
                }

                _renderer.WriteError("unknown command  " + command.Argument);
                return (true, false);
            default:
                return (false, false);
        }
    }

    private void ChangeApproval(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            _approval = ApprovalMode.Next(_approval);
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

        _settings = _settings.WithApproval(_approval);
        _settingsStore.Save(_settings);
        RebuildExecutors();
        RefreshChrome();
        _renderer.WriteNote("approval  " + ApprovalLabel.For(_approval));
    }

    private void ChangeThinking(string argument)
    {
        var model = _settings.ActiveModel;
        if (!model.Thinking)
        {
            _renderer.WriteError("The selected model does not support thinking.");
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            _thinkingEffort = ThinkingSelection.Next(_thinkingEffort, model);
        }
        else
        {
            ThinkingSelection selection;
            try
            {
                selection = ThinkingSelection.Parse(argument);
            }
            catch (ArgumentException exception)
            {
                _renderer.WriteError(exception.Message);
                return;
            }

            if (selection != ThinkingSelection.Default
                && selection != ThinkingSelection.Off
                && !model.AllowsEffort(selection.Value))
            {
                _renderer.WriteError(
                    $"Thinking effort '{selection.Value}' is not available for this model.");
                return;
            }

            _thinkingEffort = selection;
        }

        _settings = _settings.WithThinkingEffort(_thinkingEffort);
        _settingsStore.Save(_settings);
        RebuildExecutors();
        RefreshChrome();
        _renderer.WriteNote("thinking  " + ThinkingLabel.For(_thinkingEffort));
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
        ReplaceLiveSystem();
        return _planMode;
    }

    private string CurrentSystemText() =>
        _planMode ? _prompts.PlanSystem : _prompts.WorkSystem;

    private void ReloadPrompts()
    {
        _prompts = _promptStore.Load(_workspace.Root);
        ReplaceLiveSystem();
    }

    private CompactionLimits CurrentLimits() =>
        new(
            _settings.ActiveModel.ContextWindow,
            _settings.ActiveModel.MaxTokens,
            _settings.CompactionThreshold);

    private async Task CompactForcedAsync(CancellationToken cancellationToken)
    {
        if (_turnActive)
        {
            _renderer.WriteError("Finish the current turn before compacting.");
            return;
        }

        await RunCompactionAsync(_transcript, silentSkip: false, cancellationToken);
    }

    private async Task CompactIfNeededAsync(TurnResult result, CancellationToken cancellationToken)
    {
        if (!ContextAccountant.ShouldCompact(
                result.Usage ?? _ledger.Usage,
                _settings.ActiveModel.ContextWindow,
                _settings.CompactionThreshold,
                _settings.ActiveModel.MaxTokens))
        {
            return;
        }

        await RunCompactionAsync(_transcript, silentSkip: true, cancellationToken);
    }

    private async Task<CompactionOutcome> CompactRoundAsync(
        IReadOnlyList<ChatItem> transcript,
        CancellationToken cancellationToken)
    {
        var limits = CurrentLimits();
        if (!ContextAccountant.ShouldCompact(
                TokenEstimator.Items(transcript),
                limits.ContextWindow,
                limits.Threshold,
                limits.MaxTokens))
        {
            return new CompactionOutcome(transcript, CompactionKind.Unchanged);
        }

        var outcome = await RunCompactionAsync(transcript, silentSkip: true, cancellationToken);
        if (outcome.Kind == CompactionKind.Unchanged)
        {
            _renderer.WriteError("Session is too large to compact.");
            return new CompactionOutcome(transcript, CompactionKind.Exhausted);
        }

        return outcome;
    }

    private async Task<CompactionOutcome> RunCompactionAsync(
        IReadOnlyList<ChatItem> transcript,
        bool silentSkip,
        CancellationToken cancellationToken)
    {
        _renderer.WriteNote("compacting context...");
        var outcome = await _compactor.CompactAsync(
            transcript,
            _todos.Format(),
            CurrentLimits(),
            cancellationToken);
        if (outcome.Kind == CompactionKind.Applied)
        {
            if (ReferenceEquals(transcript, _transcript))
            {
                _transcript = [.. outcome.Transcript];
                RememberCompactedUsage();
                SaveSession();
            }

            _renderer.WriteNote("compacted context");
            return outcome;
        }

        if (outcome.Kind == CompactionKind.Exhausted)
        {
            _renderer.WriteError("Session is too large to compact.");
            return outcome;
        }

        if (!silentSkip)
        {
            _renderer.WriteNote("compaction skipped");
        }

        return outcome;
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
                Todos = SessionMapper.WriteTodos(_todos.Snapshot()),
                UserTurns = _ledger.UserTurns,
                ModelCalls = _ledger.ModelCalls,
                ToolCalls = _ledger.ToolCalls,
                Usage = SessionMapper.WriteUsage(_ledger.Usage)
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
        ReplaceLiveSystem();

        _todos.Clear();
        _todos.Replace(SessionMapper.ReadTodos(document.Todos));
        _ledger.Restore(
            Math.Max(0, document.UserTurns),
            Math.Max(0, document.ModelCalls),
            Math.Max(0, document.ToolCalls),
            SessionMapper.ReadUsage(document.Usage));
        DiscardQueue();
        _reviewContext.CurrentUserRequest = string.Empty;
        RebuildExecutors();
        RefreshChrome();
        _renderer.ShowUsage(_ledger.Usage);
        _renderer.WriteHistory(_transcript);
        _renderer.WriteNote("resumed  " + _sessionId);
    }

    private void ReplaceLiveSystem()
    {
        if (_transcript.Count == 0)
        {
            return;
        }

        if (_transcript[0] is ChatMessage system
            && system.Role == ChatRole.System
            && !CompactionSelection.IsSummary(system))
        {
            _transcript[0] = new ChatMessage(ChatRole.System, CurrentSystemText());
        }
    }

    private void RememberCompactedUsage()
    {
        var input = TokenEstimator.Items(_transcript);
        _ledger.ReplaceUsage(new TokenUsage(input, 0));
        _renderer.ShowUsage(_ledger.Usage);
    }

    private bool HasConversation() => TranscriptCodec.HasConversation(_transcript);

    private void WriteResumeHint()
    {
        if (HasConversation())
        {
            SaveSession();
            Console.WriteLine(ResumeHint.ForSaved(_sessionId));
            return;
        }

        Console.WriteLine(ResumeHint.ForWorkspace());
    }

    private void RebuildExecutors()
    {
        var renderer = _renderer;
        var approvalPrompt = new ApprovalPrompt(renderer);
        var question = new QuestionPrompt(renderer);
        var reviewer = new ModelApprovalReviewer(
            _client,
            _prompts.Review,
            CurrentReasoning());
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

    private ReasoningOptions? CurrentReasoning() =>
        _thinkingEffort.ToReasoningOptions(_settings.ActiveModel);

    private string CurrentThinkingStatus() =>
        ThinkingStatus.For(_settings.ActiveModel, _thinkingEffort);

    private void RefreshChrome()
    {
        _renderer.SetChrome(_planMode, _approval, CurrentThinkingStatus());
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
            _renderer,
            CurrentReasoning(),
            CompactRoundAsync);
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
