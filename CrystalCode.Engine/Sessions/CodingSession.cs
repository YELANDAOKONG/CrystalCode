using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crystal;
using Crystal.Chat;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Skills;
using CrystalCode.Plugins.Hooks;
using CrystalCode.Plugins.Models;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One coding conversation: Plan/Work catalogs, approval, turns, compaction,
/// queue, and persistence. It draws nothing. A front end supplies an
/// <see cref="ISessionObserver"/> that receives <see cref="SessionEvent"/>
/// values, answers prompts, and drives the session through its public methods.
/// </summary>
public sealed class CodingSession : ITurnObserver
{
    private IStreamingChatClient _client;
    private IStreamingChatClient? _approvalClient;
    private string? _approvalClientKey;
    private IStreamingChatClient? _compactionClient;
    private string? _compactionClientKey;
    private IStreamingMultimodalChatClient? _multimodalClient;
    private IStreamingMultimodalChatClient? _imageDescriptionClient;
    private HarnessSettings _settings;

    // Disk snapshot plus operator edits. Launch overrides stay on _settings
    // unless the operator changes that field. Provider and model from the
    // command line are already on this copy.
    private HarnessSettings _persisted;
    private readonly ISettingsStore _settingsStore;
    private readonly CredentialStore _credentials;
    private readonly PromptStore _promptStore;
    private readonly SessionStore _sessionStore;
    private readonly PromptHistoryStore _promptHistoryStore;
    private ContextCompactor _compactor;
    private readonly PluginRegistry _plugins;
    private readonly SessionFrontEnd _frontEnd;
    private readonly object _usageGate = new();
    private readonly SessionReviewContext _reviewContext = new();
    private readonly SessionLedger _ledger = new();
    private readonly TodoList _todos = new();
    private readonly MessageQueue _queue = new();
    private Task _promptHistoryTail = Task.CompletedTask;
    private bool _quitRequested;
    private readonly List<string> _deferredStops = [];
    private readonly GrantStore _grants;
    private readonly WorkspaceTrustStore _trust;
    private readonly CrystalHome _home;
    private readonly SkillDiscovery _skillDiscovery;
    private readonly bool _replayOnStart;
    private Workspace _workspace;
    private PromptSet _prompts;
    private PromptResolution _promptResolution;
    private SkillCatalog? _skills;
    private ExternalCatalog _external = ExternalCatalog.Empty;
    private PluginCatalog _loadedPlugins = PluginCatalog.Empty;
    private PluginPlaceholderTable _placeholders = PluginPlaceholderTable.Empty;
    private PluginStatusBoard _statusBoard = new(static _ => { }, static _ => { });
    private bool _statusAttached;
    private readonly PluginModelView _modelView;
    private readonly PluginClientView _clientView;
    private PluginHookPipeline _hooks = PluginHookPipeline.Empty;
    private bool _pluginSessionOpen;
    private readonly SessionToolHost _toolHost;
    private readonly BackgroundTaskRegistry _backgroundTasks;
    private ApprovalMode _approval;
    private ThinkingSelection _thinkingEffort;
    private bool _planMode;
    private List<ChatItem> _transcript;
    private readonly object _archiveGate = new();
    private List<ChatItem> _archive = [];
    private Dictionary<int, ImageAttachment> _images = [];
    private readonly object _imagesGate = new();
    private List<SessionImageDocument> _unavailableImages = [];
    private readonly List<int> _pendingImages = [];
    private readonly HashSet<int> _draftImages = [];
    private int _nextImageNumber = 1;
    private string _sessionId;
    private DateTimeOffset _sessionCreatedUtc;
    private IToolExecutor _workExecutor = null!;
    private IToolExecutor _planExecutor = null!;
    private IMultimodalToolExecutor _workMultimodalExecutor = null!;
    private IMultimodalToolExecutor _planMultimodalExecutor = null!;
    private Task<TurnResult>? _turnTask;
    private CancellationTokenSource? _turnSource;
    private CancellationTokenSource? _compactSource;
    private bool _compacting;
    private bool _turnActive;
    private string? _turnUserText;
    private string? _turnFault;
    private readonly object _sideGate = new();
    private readonly List<SideExchange> _sideExchanges = [];
    private CancellationTokenSource? _sideSource;
    private Task? _sideTask;
    private long _sideGeneration;
    private bool _sideRunning;
    private string? _sidePending;
    private string _sideLive = string.Empty;
    private string? _sideFailure;
    private bool _sideThinking;
    private TokenUsage? _shownUsage;
    private TokenUsage? _shownCumulative;
    private TokenUsage? _turnCumulativeBaseline;
    private bool _promptHistoryAvailable = true;

    private CodingSession(
        HarnessSettings settings,
        ISettingsStore settingsStore,
        CredentialStore credentials,
        CrystalHome home,
        Workspace workspace,
        SessionFrontEnd frontEnd,
        PluginRegistry plugins,
        SessionDocument? resume,
        HarnessSettings? persistedSettings,
        IReadOnlyList<DirectoryAccess>? launchAccess)
    {
        ArgumentNullException.ThrowIfNull(frontEnd);
        ArgumentNullException.ThrowIfNull(frontEnd.Trust);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(credentials);
        _settings = settings;
        _persisted = persistedSettings ?? settings;
        _settingsStore = settingsStore;
        _credentials = credentials;
        _promptStore = new PromptStore(home);
        _sessionStore = new SessionStore(home);
        _promptHistoryStore = new PromptHistoryStore(home, workspace.Root);
        _workspace = workspace;
        _plugins = plugins;
        _frontEnd = frontEnd;
        _approval = settings.Approval;
        _toolHost = new SessionToolHost(
            _workspace,
            home,
            () => _sessionId ?? string.Empty,
            () => _approval.Value);
        _backgroundTasks = new BackgroundTaskRegistry(
            _workspace,
            settings.BackgroundBashTimeoutSeconds);
        _thinkingEffort = settings.ThinkingEffort;
        _grants = new GrantStore(home);
        _trust = new WorkspaceTrustStore(home);
        _home = home;
        _skillDiscovery = SkillDiscovery.Create(home);
        _modelView = new PluginModelView(CurrentPluginModel, CurrentPluginReview);
        _clientView = new PluginClientView(
            () => _settings.ApprovalModel.Enabled,
            CreateSideSessionClient,
            CreateSideImageClient,
            CreateSideReviewClient);
        ReloadPlugins();
        _client = CreateClient(settings);
        _multimodalClient = CreateMultimodalClient(settings);
        BindCompactor();
        _promptResolution = ResolvePrompts();
        _prompts = _promptResolution.Prompts;
        ReloadSkills();
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
        _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
        _replayOnStart = resume is not null;
        if (resume is not null)
        {
            ApplyDocument(resume);
        }
        else
        {
            BindReviewConversation();
            RebuildExecutors();
        }

        if (launchAccess is { Count: > 0 })
        {
            ApplyLaunchAccess(launchAccess);
        }
        else if (resume is null)
        {
            SeedWorkspaceDefaults();
        }
    }

    public static CodingSession Create(
        HarnessSettings settings,
        ISettingsStore settingsStore,
        CredentialStore credentials,
        CrystalHome home,
        string workspaceRoot,
        SessionFrontEnd frontEnd,
        PluginRegistry? plugins = null,
        SessionDocument? resume = null,
        HarnessSettings? persistedSettings = null,
        IReadOnlyList<DirectoryAccess>? launchAccess = null)
    {
        if (resume is not null && !DirectoryAccessPolicy.TryValidateSaved(resume.Access, out var error))
        {
            throw new ArgumentException(error, nameof(resume));
        }

        return new CodingSession(
            settings,
            settingsStore,
            credentials,
            home,
            new Workspace(workspaceRoot),
            frontEnd,
            plugins ?? PluginRegistry.CreateBuiltIn(),
            resume,
            persistedSettings,
            launchAccess);
    }

    /// <summary>
    /// True while a turn task is running or has finished and not yet been
    /// collected with <see cref="FinishTurnAsync"/>.
    /// </summary>
    public bool TurnActive => _turnActive;

    /// <summary>
    /// The running turn, or null when idle. Await it, then call
    /// <see cref="CompleteTurnAsync"/>.
    /// </summary>
    public Task? TurnTask => _turnTask;

    public bool PlanMode => _planMode;

    /// <summary>
    /// Id of the saved session file. It stays stable for this process.
    /// </summary>
    public string SessionId => _sessionId;

    /// <summary>
    /// Loads prompt history, publishes the opening state, and loads operator
    /// tools. Call once, after the front end is ready to receive events.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadPromptHistoryAsync(cancellationToken);
        PublishPreferences();
        RefreshSlashCommands();
        Publish(new SessionStarted(CurrentChrome()));
        if (_replayOnStart)
        {
            PresentResume();
        }

        WritePromptNotes();
        if (!string.IsNullOrWhiteSpace(CurrentPromptStatus()))
        {
            Note("Prompt set  " + _promptResolution.PromptSet);
        }

        var attachmentStatus = PromptAttachmentText.Status(_promptResolution);
        if (attachmentStatus.Length > 0)
        {
            Note("Prompt attachments  " + attachmentStatus);
        }

        var disabledTools = BuiltInToolSelection.OperatorNote(_settings.DisabledBuiltInTools);
        if (disabledTools.Length > 0)
        {
            Note(disabledTools);
        }

        ReloadExternalToolsWithProgress();
        PublishPluginEnvironment();
        RebuildExecutors();
        ReplaceLiveSystem();
        WritePluginNotes();
        WriteExternalNotes();
        await OpenPluginSessionAsync(cancellationToken);
        ShowTodos();
    }

    /// <summary>
    /// Handles one submitted prompt: a slash command, a follow-up while a turn
    /// runs, or the start of a turn. Empty text while a turn runs interrupts
    /// when a follow-up is queued, and is ignored otherwise.
    /// A command that would change the session is refused while compaction
    /// runs. Commands that only report state still run.
    /// Returns true when the operator asked to quit.
    /// </summary>
    public async Task<bool> SubmitAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            if (await SubmitCoreAsync(text, cancellationToken))
            {
                ClearDeferredStops();
                return true;
            }

            return await DrainDeferredStopAsync(cancellationToken);
        }
        finally
        {
            await FlushPromptHistoryAsync();
        }
    }

    private async Task<bool> SubmitCoreAsync(string text, CancellationToken cancellationToken)
    {
        await SavePromptHistoryAsync(text, cancellationToken);
        var input = text.Trim();
        PruneDraftImages(input);
        if (RefuseCompactionCommand(input))
        {
            return false;
        }

        if (QuitDuringCompaction(input))
        {
            if (_turnActive)
            {
                _turnSource?.Cancel();
                await FinishTurnAsync(cancellationToken);
            }
            else
            {
                _quitRequested = true;
                TryInterrupt();
            }

            return true;
        }

        if (_turnActive)
        {
            if (input.Length > 0)
            {
                if (StopsBusyTurn(input))
                {
                    _turnSource?.Cancel();
                    await FinishTurnAsync(cancellationToken);
                }

                var busy = await TryHandleCommandAsync(input, cancellationToken);
                if (busy.Handled)
                {
                    return busy.Exit;
                }

                AcceptQueuedInput(input, remember: false);
                return false;
            }

            ReleaseQueuedFollowUp();
            return false;
        }

        if (input.Length == 0)
        {
            return false;
        }

        var command = await TryHandleCommandAsync(input, cancellationToken);
        if (command.Handled)
        {
            return command.Exit;
        }

        await StartTurnAsync(input, cancellationToken);
        return false;
    }

    /// <summary>
    /// Sends a text-only template without parsing commands or consuming the unsent draft.
    /// A pump queues instead of starting a concurrent turn during other session work.
    /// </summary>
    public async Task SubmitQuickPromptAsync(
        string text,
        bool queueOnly,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var error = QuickPromptStore.ValidateText(text);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(text));
        }

        await SavePromptHistoryAsync(text, cancellationToken);
        if (_turnActive || queueOnly)
        {
            _queue.Enqueue(text, interruptAfterTools: false, preserveText: true);
            ShowQueue();
            return;
        }

        await StartTurnAsync(text, cancellationToken, attachPendingImages: false);
    }

    /// <summary>
    /// Collects a finished turn, then starts the queued follow-up if any.
    /// Returns true when the operator asked to quit while that work ran.
    /// </summary>
    public async Task<bool> CompleteTurnAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FinishTurnAsync(cancellationToken);
            if (ConsumeQuit())
            {
                ClearDeferredStops();
                return true;
            }

            if (await DrainDeferredStopAsync(cancellationToken))
            {
                return true;
            }

            await StartTurnIfQueuedAsync(cancellationToken);
            if (ConsumeQuit())
            {
                ClearDeferredStops();
                return true;
            }

            return await DrainDeferredStopAsync(cancellationToken);
        }
        finally
        {
            await FlushPromptHistoryAsync();
        }
    }

    /// <summary>
    /// Cancels the running turn or compaction. Returns false when there is
    /// nothing to cancel.
    /// </summary>
    public bool TryInterrupt()
    {
        if (_turnActive && _turnSource is not null)
        {
            _turnSource.Cancel();
            return true;
        }

        if (_compactSource is not null)
        {
            _compactSource.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Switches between Plan and Work, replaces the live system message, and
    /// returns the new mode.
    /// </summary>
    public bool TogglePlan()
    {
        _planMode = !_planMode;
        ReplaceLiveSystem();
        RefreshChrome();
        return _planMode;
    }

    /// <summary>
    /// Records a verbose preference chosen from a front-end control and
    /// persists it.
    /// </summary>
    public void SetVerbose(VerboseTarget target, bool enabled)
    {
        SaveSettings(current => target switch
        {
            VerboseTarget.Tools => current.WithVerboseTools(enabled),
            VerboseTarget.Commands => current.WithVerboseCommands(enabled),
            VerboseTarget.Results => current.WithVerboseResults(enabled),
            VerboseTarget.Calls => current.WithVerboseCalls(enabled),
            VerboseTarget.Approvals => current.WithVerboseApprovals(enabled),
            VerboseTarget.Review => current.WithVerboseReview(enabled),
            VerboseTarget.Thinking => current.WithVerboseThinking(enabled),
            _ => current
        });
        PublishPreferences();
    }

    private void SaveSettings(Func<HarnessSettings, HarnessSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _settings = change(_settings);
        _persisted = change(_persisted);
        _settingsStore.Save(_persisted);
    }

    /// <summary>
    /// Reports the current unsent draft so attachments the operator deleted
    /// are discarded. Ignored while a turn runs.
    /// </summary>
    public void NotifyDraftChanged(string draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!_turnActive)
        {
            PruneDraftImages(draft);
        }
    }

    /// <summary>
    /// Persists a conversation that has content, releases the model clients,
    /// and returns the copy that tells the operator how to resume.
    /// </summary>
    public async Task<string> CloseAsync(CancellationToken cancellationToken = default)
    {
        await ClosePluginSessionAsync(cancellationToken);
        await _backgroundTasks.StopAllAsync();
        await FlushPromptHistoryAsync();
        return Close();
    }

    public string Close()
    {
        CancelAndClearSide(announce: false);
        try
        {
            PruneDraftImages(string.Empty, includeQueue: false);
            if (HasConversation())
            {
                SaveSession();
                return ResumeHint.ForSaved(_sessionId);
            }

            return ResumeHint.ForWorkspace();
        }
        finally
        {
            _backgroundTasks.Dispose();
            DisposeClient();
        }
    }

    private async Task LoadPromptHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            Publish(new PromptHistoryLoaded(await _promptHistoryStore.LoadAsync(cancellationToken)));
        }
        catch (IOException)
        {
            _promptHistoryAvailable = false;
            Note("Prompt history is unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            _promptHistoryAvailable = false;
            Note("Prompt history is unavailable");
        }
    }

    private async Task SavePromptHistoryAsync(string text, CancellationToken cancellationToken)
    {
        if (!_promptHistoryAvailable)
        {
            return;
        }

        try
        {
            await _promptHistoryStore.AppendAsync(text, cancellationToken);
        }
        catch (IOException)
        {
            _promptHistoryAvailable = false;
            Note("Prompt history could not be saved");
        }
        catch (UnauthorizedAccessException)
        {
            _promptHistoryAvailable = false;
            Note("Prompt history could not be saved");
        }
    }

    private void RememberPrompt(string text)
    {
        if (!_promptHistoryAvailable || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var previous = _promptHistoryTail;
        _promptHistoryTail = RememberPromptAfterAsync(previous, text);
    }

    private async Task RememberPromptAfterAsync(Task previous, string text)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // The earlier write already reported the failure.
        }

        await SavePromptHistoryAsync(text, CancellationToken.None);
    }

    private async Task FlushPromptHistoryAsync()
    {
        while (true)
        {
            var pending = _promptHistoryTail;
            try
            {
                await pending;
            }
            catch (Exception)
            {
                // SavePromptHistoryAsync already noted the failure.
            }

            if (ReferenceEquals(pending, _promptHistoryTail))
            {
                return;
            }
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
            if (!string.IsNullOrWhiteSpace(parsed.Argument))
            {
                if (CompactionModelArguments.IsModelCommand(parsed.Argument))
                {
                    if (!CompactionModelArguments.TryParse(
                            parsed.Argument,
                            out var request,
                            out var parseError))
                    {
                        Error(parseError);
                    }
                    else
                    {
                        ChangeCompactionModel(request);
                    }
                }
                else if (CompactionThinkingArguments.IsThinkingCommand(parsed.Argument))
                {
                    ChangeCompactionThinking(CompactionThinkingArguments.Parse(parsed.Argument));
                }
                else if (CompactionSummaryArguments.IsSummaryCommand(parsed.Argument)
                    || CompactionSummaryArguments.IsAutomaticCommand(parsed.Argument))
                {
                    var automatic = CompactionSummaryArguments.IsAutomaticCommand(parsed.Argument);
                    if (!CompactionSummaryArguments.TryParse(
                            parsed.Argument,
                            automatic,
                            out var request,
                            out var parseError))
                    {
                        Error(parseError);
                    }
                    else
                    {
                        ChangeCompactionSummary(request);
                    }
                }
                else if (CompactionMicroArguments.IsMicroCommand(parsed.Argument))
                {
                    if (!CompactionMicroArguments.TryParse(
                            parsed.Argument,
                            out var enabled,
                            out var parseError))
                    {
                        Error(parseError);
                    }
                    else
                    {
                        ChangeMicroCompaction(enabled);
                    }
                }
                else
                {
                    Error("Compact accepts no argument, or model, thinking, summary, auto, and micro.");
                }

                PruneDraftImages(string.Empty);
                return (true, false);
            }

            try
            {
                await CompactForcedAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Note("Compaction cancelled");
            }

            if (ConsumeQuit())
            {
                PruneDraftImages(string.Empty);
                return (true, true);
            }

            var exit = await StartTurnIfQueuedAsync(cancellationToken);
            PruneDraftImages(string.Empty);
            return (true, exit);
        }

        if (parsed.Verb == SessionVerb.Resume)
        {
            await ResumeSessionAsync(parsed.Argument, cancellationToken);
            PruneDraftImages(string.Empty);
            return (true, false);
        }

        var handled = await HandleCommandAsync(parsed, cancellationToken);
        PruneDraftImages(string.Empty);
        return handled;
    }

    private async Task<(bool Handled, bool Exit)> HandleCommandAsync(
        SessionCommand command,
        CancellationToken cancellationToken)
    {
        switch (command.Verb)
        {
            case SessionVerb.Help:
                Publish(new HelpRequested(PluginCommands()));
                return (true, false);
            case SessionVerb.Plan:
                TogglePlan();
                Note(ModeLabel.For(_planMode));
                return (true, false);
            case SessionVerb.Approval:
                ChangeApproval(command.Argument);
                return (true, false);
            case SessionVerb.Context:
                ChangeContext(command.Argument);
                return (true, false);
            case SessionVerb.Tier:
                ChangeCatalogSlot(command.Argument, serviceTier: true);
                return (true, false);
            case SessionVerb.Speed:
                ChangeCatalogSlot(command.Argument, serviceTier: false);
                return (true, false);
            case SessionVerb.Thinking:
                ChangeThinking(command.Argument);
                return (true, false);
            case SessionVerb.Tokens:
                ChangeEstimatedTokens(command.Argument);
                return (true, false);
            case SessionVerb.Verbose:
                ChangeVerbose(command.Argument);
                return (true, false);
            case SessionVerb.Model:
                ChangeModel(command.Argument);
                return (true, false);
            case SessionVerb.PromptSet:
                ChangePromptSet(command.Argument);
                return (true, false);
            case SessionVerb.PromptAttachment:
                ChangePromptAttachments(command.Argument);
                return (true, false);
            case SessionVerb.Status:
                ShowStatus(command.Argument);
                return (true, false);
            case SessionVerb.Stats:
                ShowStats(command.Argument);
                return (true, false);
            case SessionVerb.Btw:
                if (string.IsNullOrWhiteSpace(command.Argument))
                {
                    ShowSideQuestion();
                }
                else
                {
                    AskSide(command.Argument.Trim());
                }

                return (true, false);
            case SessionVerb.StatusLine:
                ChangeStatusLine(command.Argument);
                return (true, false);
            case SessionVerb.Clear:
                await RebindPluginSessionAsync(BeginNewSession, cancellationToken);
                Publish(new ConversationCleared());
                ShowUsage(null, null);
                Note("New conversation");
                return (true, false);
            case SessionVerb.Access:
                ChangeAccess(command.Argument);
                return (true, false);
            case SessionVerb.Cd:
                await ChangeDirectoryAsync(command.Argument, cancellationToken);
                return (true, false);
            case SessionVerb.Space:
                await ChangeSpaceAsync(command.Argument, cancellationToken);
                return (true, false);
            case SessionVerb.Trust:
                return (true, await ChangeTrustAsync(command.Argument, cancellationToken));
            case SessionVerb.Rules:
                ChangeRules(command.Argument);
                return (true, false);
            case SessionVerb.Fork:
                await ForkSessionAsync(command.Argument, cancellationToken);
                return (true, false);
            case SessionVerb.Sessions:
                ShowSessions(command.Argument);
                return (true, false);
            case SessionVerb.Compact:
                return (true, false);
            case SessionVerb.Todos:
                ShowTodoList();
                return (true, false);
            case SessionVerb.Tools:
                ChangeTools(command.Argument);
                return (true, false);
            case SessionVerb.Plugins:
                await ChangePluginsAsync(command.Argument, cancellationToken);
                return (true, false);
            case SessionVerb.Attach:
                AttachImage(command.Argument);
                return (true, false);
            case SessionVerb.Export:
                ExportSession(command.Argument);
                return (true, false);
            case SessionVerb.Quit:
                return (true, true);
            case SessionVerb.Unknown:
                if (_plugins.TryExecute(command.Argument, new SlashOutput(this))
                    || _loadedPlugins.TryExecute(command.Argument, new SlashOutput(this)))
                {
                    return (true, false);
                }

                Error("Unknown command  " + command.Argument);
                return (true, false);
            default:
                return (false, false);
        }
    }

    /// <summary>
    /// Adds or replaces a session directory rule. This does not bypass action approval
    /// and does not sandbox shell processes or extensions.
    /// </summary>
    public bool TrySetDirectoryAccess(string path, string mode, out string error)
    {
        error = string.Empty;
        if (_turnActive || _compactSource is not null)
        {
            error = "Finish the current turn before changing directory access.";
            return false;
        }

        if (!TryResolveDirectoryAccess(path, mode, out var rule, out error))
        {
            return false;
        }

        _workspace.Access.Set(rule.Path, rule.Mode, rule.SourcePath);
        ReplaceLiveSystem();
        SaveSession();
        return true;
    }

    private bool TryResolveDirectoryAccess(string path, string mode, out DirectoryAccess rule, out string error)
    {
        rule = null!;
        error = string.Empty;
        if (mode is not ("read" or "write"))
        {
            error = "Access mode must be read or write.";
            return false;
        }

        if (!_workspace.TryResolve(path, out var fullPath, out error)
            || !_workspace.TryGetEntryPath(path, out var sourcePath, out error))
        {
            return false;
        }

        if (Workspace.IsCredentialPath(path) || Workspace.IsCredentialPath(fullPath))
        {
            error = "Credential directories cannot be granted access.";
            return false;
        }

        rule = new DirectoryAccess(Path.TrimEndingDirectorySeparator(fullPath), mode,
            Path.TrimEndingDirectorySeparator(sourcePath));
        return true;
    }

    private void ApplyLaunchAccess(IReadOnlyList<DirectoryAccess> launchAccess)
    {
        var initialAccess = new List<DirectoryAccess>();
        foreach (var rule in launchAccess)
        {
            if (rule is null || !TryResolveDirectoryAccess(rule.Path, rule.Mode, out var resolved, out _))
            {
                throw new ArgumentException("Launch directory access is invalid.", nameof(launchAccess));
            }

            resolved = resolved with { SourcePath = rule.SourcePath ?? resolved.SourcePath };
            if (!DirectoryAccessPolicy.TryValidateSaved([resolved], out _))
            {
                throw new ArgumentException("Launch directory access is invalid.", nameof(launchAccess));
            }

            initialAccess.Add(resolved);
        }

        foreach (var rule in initialAccess)
        {
            _workspace.Access.Set(rule.Path, rule.Mode, rule.SourcePath);
        }

        if (initialAccess.Count > 0)
        {
            ReplaceLiveSystem();
            SaveSession();
        }
    }

    /// <summary>
    /// Copies this workspace's Home defaults into the session. Flags and
    /// resume do not call this. Skipped entries stay in the file.
    /// </summary>
    private void SeedWorkspaceDefaults()
    {
        var store = new WorkspaceAccessStore(_home);
        if (!store.TrySeed(_workspace.Root, out var rules, out var error, out var skipped))
        {
            Error(error);
            return;
        }

        foreach (var skippedError in skipped)
        {
            Error(skippedError);
        }

        if (rules.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            _workspace.Access.Set(rule.Path, rule.Mode, rule.SourcePath);
        }

        ReplaceLiveSystem();
        SaveSession();
    }

    private const string AccessUsage =
        "Use /access, /access add <path> read|write, /access remove <path>, /access default, /access default add <path> read|write, or /access default remove <path>.";

    private void ChangeAccess(string argument)
    {
        var text = argument.Trim();
        if (text.Length == 0)
        {
            Note("Workspace  " + _workspace.Access.ModeFor(_workspace.Root, _workspace.Root) + "  " + _workspace.Root);
            foreach (var rule in _workspace.Access.Snapshot())
            {
                Note("Access  " + rule.Mode + "  " + rule.Path);
            }

            Note("Directory access applies to built-in file tools, not shell or extension process isolation.");
            return;
        }

        if (text.Equals("default", StringComparison.Ordinal)
            || text.StartsWith("default ", StringComparison.Ordinal))
        {
            var rest = text.Length == "default".Length
                ? string.Empty
                : text["default".Length..].Trim();
            ChangeDefaultAccess(rest);
            return;
        }

        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before changing directory access.");
            return;
        }

        if (text.StartsWith("add ", StringComparison.Ordinal))
        {
            if (!TrySplitAccessAdd(text, out var path, out var mode))
            {
                Error(AccessUsage);
                return;
            }

            if (!TrySetDirectoryAccess(path, mode, out var error))
            {
                Error(error);
                return;
            }

            Note("Access updated  " + mode + "  " + path);
            return;
        }

        if (text.StartsWith("remove ", StringComparison.Ordinal))
        {
            var path = UnquoteAccessPath(text[7..]);
            if (!_workspace.TryGetEntryPath(path, out var entryPath, out var error))
            {
                Error(error);
                return;
            }

            var removed = _workspace.Access.RemoveByEntryPath(Path.TrimEndingDirectorySeparator(entryPath));
            if (!removed)
            {
                if (!_workspace.TryGetFullPath(path, out var fullPath, out error))
                {
                    Error(error);
                    return;
                }

                removed = _workspace.Access.Remove(Path.TrimEndingDirectorySeparator(fullPath));
            }

            if (!removed)
            {
                Error("No session access rule for that directory.");
                return;
            }

            ReplaceLiveSystem();
            SaveSession();
            Note("Access removed  " + entryPath);
            return;
        }

        Error(AccessUsage);
    }

    private void ChangeDefaultAccess(string text)
    {
        var store = new WorkspaceAccessStore(_home);
        if (text.Length == 0)
        {
            if (!store.TryList(_workspace.Root, out var rules, out var listError))
            {
                Error(listError);
                return;
            }

            if (rules.Count == 0)
            {
                Note("No default directories for this workspace.");
                return;
            }

            foreach (var rule in rules)
            {
                Note("Default  " + rule.Mode + "  " + rule.Path);
            }

            return;
        }

        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before changing directory access.");
            return;
        }

        if (text.StartsWith("add ", StringComparison.Ordinal))
        {
            if (!TrySplitAccessAdd(text, out var path, out var mode))
            {
                Error(AccessUsage);
                return;
            }

            if (!TryResolveDirectoryAccess(path, mode, out var rule, out var resolveError))
            {
                Error(resolveError);
                return;
            }

            if (!store.TrySet(_workspace.Root, rule, out var saveError))
            {
                Error(saveError);
                return;
            }

            if (!TrySetDirectoryAccess(path, mode, out var sessionError))
            {
                Error(sessionError);
                return;
            }

            Note("Default access updated  " + mode + "  " + path);
            return;
        }

        if (text.StartsWith("remove ", StringComparison.Ordinal))
        {
            var path = UnquoteAccessPath(text[7..]);
            if (!_workspace.TryGetEntryPath(path, out var entryPath, out var pathError))
            {
                Error(pathError);
                return;
            }

            entryPath = Path.TrimEndingDirectorySeparator(entryPath);
            string? canonical = null;
            if (_workspace.TryGetFullPath(path, out var fullPath, out _))
            {
                canonical = Path.TrimEndingDirectorySeparator(fullPath);
            }

            if (!store.TryRemove(_workspace.Root, entryPath, canonical, out var removed, out var removeError))
            {
                Error(removeError);
                return;
            }

            if (!removed)
            {
                Error("No default access rule for that directory.");
                return;
            }

            var sessionRemoved = _workspace.Access.RemoveByEntryPath(entryPath);
            if (!sessionRemoved && canonical is not null)
            {
                sessionRemoved = _workspace.Access.Remove(canonical);
            }

            if (sessionRemoved)
            {
                ReplaceLiveSystem();
                SaveSession();
            }

            Note("Default access removed  " + entryPath);
            return;
        }

        Error(AccessUsage);
    }

    private static bool TrySplitAccessAdd(string text, out string path, out string mode)
    {
        path = string.Empty;
        mode = string.Empty;
        var split = text.LastIndexOf(' ');
        if (split <= "add ".Length)
        {
            return false;
        }

        path = UnquoteAccessPath(text[4..split]);
        mode = text[(split + 1)..];
        return true;
    }

    private static string UnquoteAccessPath(string path)
    {
        var trimmed = path.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private void ChangeApproval(string argument)
    {
        if (ApprovalModelArguments.IsModelCommand(argument))
        {
            if (!ApprovalModelArguments.TryParse(argument, out var request, out var parseError))
            {
                Error(parseError);
                return;
            }

            ChangeApprovalModel(request);
            return;
        }

        if (ApprovalThinkingArguments.IsThinkingCommand(argument))
        {
            ChangeApprovalThinking(ApprovalThinkingArguments.Parse(argument));
            return;
        }

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
                Error(exception.Message);
                return;
            }
        }

        SaveSettings(current => current.WithApproval(_approval));
        RebuildExecutors();
        ReplaceLiveSystem();
        RefreshChrome();
        Note("Approval  " + ApprovalLabel.For(_approval));
    }

    private void ChangeApprovalModel(ApprovalModelArguments.Request request)
    {
        if (request.Show)
        {
            Note(_settings.ApprovalModel.Describe());
            return;
        }

        if (_turnActive)
        {
            Error("Finish the current turn before changing the approval model.");
            return;
        }

        ApprovalModelSettings next;
        if (request.Enabled is bool enabled)
        {
            if (enabled && !_settings.ApprovalModel.HasSelection)
            {
                Error("Set an approval model before turning it on.");
                return;
            }

            next = enabled
                ? _settings.ApprovalModel.EnabledCopy()
                : _settings.ApprovalModel.DisabledCopy();
        }
        else if (!TrySelectApprovalModel(request.Selection ?? string.Empty, out next, out var selectError))
        {
            Error(selectError);
            return;
        }

        if (!TryPrepareApprovalClient(next, out var client, out var prepareError))
        {
            Error(prepareError);
            return;
        }

        // A launch gear is process-only. This command changes the switch or
        // the selection, so the saved gear stays whatever config.json held.
        var persistedNext = next.WithThinkingEffort(
            _persisted.ApprovalModel.ThinkingEffort);
        HarnessSettings updated;
        try
        {
            updated = _settings.WithApprovalModel(next);
            _ = _persisted.WithApprovalModel(persistedNext);
        }
        catch (InvalidOperationException exception)
        {
            if (client is not null && !ReferenceEquals(client, _client))
            {
                DisposeClient(client);
            }

            Error(exception.Message);
            return;
        }

        _settings = updated;
        _persisted = _persisted.WithApprovalModel(persistedNext);
        _settingsStore.Save(_persisted);
        ReplaceApprovalClient(next, client);
        RebuildExecutors();
        RefreshSlashCommands();
        Note(next.Describe());
    }

    private void ChangeApprovalThinking(ApprovalThinkingArguments.Request request)
    {
        var approval = _settings.ApprovalModel;
        if (!approval.HasSelection)
        {
            Error("Set an approval model before changing its thinking gear.");
            return;
        }

        ModelSettings model;
        try
        {
            model = _settings.Catalog.GetModel(
                new ProviderName(approval.Provider!),
                approval.Model!);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            Error(exception.Message);
            return;
        }

        if (!model.Thinking)
        {
            Error("The approval model does not support thinking.");
            return;
        }

        var selection = approval.ThinkingEffort;
        if (request.Effort is null)
        {
            selection = ThinkingSelection.Next(selection, model);
        }
        else
        {
            ThinkingSelection parsed;
            try
            {
                parsed = ThinkingSelection.Parse(request.Effort);
            }
            catch (ArgumentException exception)
            {
                Error(exception.Message);
                return;
            }

            if (parsed == ThinkingSelection.Off && !model.ThinkingCanDisable)
            {
                Error("The approval model cannot disable thinking.");
                return;
            }

            if (parsed != ThinkingSelection.Default
                && parsed != ThinkingSelection.Off
                && !model.AllowsEffort(parsed.Value))
            {
                Error(
                    $"Thinking effort '{parsed.Value}' is not available for the approval model.");
                return;
            }

            selection = parsed;
        }

        SaveSettings(current => current.WithApprovalModel(
            current.ApprovalModel.WithThinkingEffort(selection)));
        RebuildExecutors();
        RefreshSlashCommands();
        Note("Approval thinking  " + ThinkingLabel.For(selection));
    }

    private bool TrySelectApprovalModel(
        string selectionText,
        out ApprovalModelSettings settings,
        out string error)
    {
        settings = ApprovalModelSettings.Off;
        var currentProvider = _settings.ApprovalModel.Provider is string stored
            ? new ProviderName(stored)
            : _settings.Provider;
        if (!ModelSelection.TryResolve(
                _settings.Catalog,
                currentProvider,
                selectionText,
                out var selection,
                out error)
            || selection is null)
        {
            error = (error ?? string.Empty).Replace(
                "/model",
                "/approval model",
                StringComparison.Ordinal);
            return false;
        }

        settings = new ApprovalModelSettings(
            true,
            selection.Provider.Value,
            selection.Model,
            _settings.ApprovalModel.ThinkingEffort);
        error = string.Empty;
        return true;
    }

    private bool TryPrepareApprovalClient(
        ApprovalModelSettings settings,
        out IStreamingChatClient? client,
        out string error)
    {
        client = null;
        error = string.Empty;
        if (!settings.Enabled)
        {
            return true;
        }

        HarnessSettings approvalSettings;
        try
        {
            approvalSettings = _settings.WithSelection(
                new ProviderName(settings.Provider!),
                settings.Model!);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException)
        {
            error = exception.Message;
            return false;
        }

        if (!_credentials.TryResolve(approvalSettings.ActiveProvider, out var apiKey, out error))
        {
            return false;
        }

        try
        {
            client = ChatClientFactory.Create(approvalSettings, apiKey, _plugins);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            error = exception.Message;
            return false;
        }
    }

    private void ReplaceApprovalClient(ApprovalModelSettings settings, IStreamingChatClient? client)
    {
        ReleaseApprovalClient();
        _clientView.ReleaseReview();
        if (client is null)
        {
            return;
        }

        _approvalClient = client;
        _approvalClientKey = ApprovalClientKey(settings);
    }

    private void ChangeContext(string argument)
    {
        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before switching context windows.");
            return;
        }

        var windows = _settings.ActiveModel.ContextWindows;
        int selected;
        if (string.IsNullOrWhiteSpace(argument))
        {
            var index = Array.IndexOf(windows.ToArray(), _settings.ContextWindow);
            selected = windows[(index + 1) % windows.Count];
        }
        else if (!ContextWindowSelection.TryParse(argument, out selected) || !windows.Contains(selected))
        {
            Error("Context window is not available for this model. Available: "
                + string.Join(", ", windows.Select(ContextWindowSelection.Format)));
            return;
        }

        var provider = _settings.Provider;
        var model = _settings.Model;
        SaveSettings(current => current.WithContextWindow(provider, model, selected));
        RefreshChrome();
        ShowUsage(ShownUsage(), ShownCumulative());
        Note("Context  " + ContextWindowSelection.Format(selected));
    }

    private void ChangeCatalogSlot(string argument, bool serviceTier)
    {
        if (_turnActive || _compactSource is not null)
        {
            Error(
                "Finish the current turn before switching "
                + (serviceTier ? "service tier." : "speed."));
            return;
        }

        var model = _settings.ActiveModel;
        var tokens = serviceTier ? model.ServiceTiers : model.Speeds;
        var label = serviceTier ? "Service tier" : "Speed";
        if (tokens.Count == 0)
        {
            Error(label + " is not available for this model.");
            return;
        }

        string? selected;
        if (string.IsNullOrWhiteSpace(argument))
        {
            var current = serviceTier ? _settings.ServiceTier : _settings.Speed;
            selected = CatalogSlots.Next(current, tokens);
        }
        else if (!CatalogToken.TryNormalize(argument, out var token) || !CatalogSlots.Contains(tokens, token))
        {
            Error(
                label + " is not available for this model. Available: " + string.Join(", ", tokens));
            return;
        }
        else
        {
            selected = token;
        }

        var provider = _settings.Provider;
        var modelName = _settings.Model;
        var next = serviceTier
            ? _settings.WithTier(provider, modelName, selected)
            : _settings.WithSpeed(provider, modelName, selected);
        if (!TryReplaceWorkClient(next, out var error))
        {
            Error(error);
            return;
        }

        SaveSettings(current => serviceTier
            ? current.WithTier(provider, modelName, selected)
            : current.WithSpeed(provider, modelName, selected));
        if (!RefreshSideClients(provider, modelName, out var sideError))
        {
            Error(sideError);
        }

        RefreshSlashCommands();
        RefreshChrome();
        Note(
            (serviceTier ? "Tier  " : "Speed  ")
            + (selected is null ? "not set" : DisplayCase.Token(selected)));
    }

    private bool TryReplaceWorkClient(HarnessSettings next, out string error)
    {
        IStreamingChatClient? nextClient = null;
        IStreamingMultimodalChatClient? nextMultimodal;
        try
        {
            nextClient = CreateClient(next);
            nextMultimodal = CreateMultimodalClient(next);
            if ((_pendingImages.Count > 0 || HasReferencedImages()) && nextMultimodal is null)
            {
                DisposeClient(nextClient);
                error = "The selected model cannot continue a session containing images.";
                return false;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisposeClient(nextClient);
            error = exception.Message;
            return false;
        }

        var previous = _client;
        var previousMultimodal = _multimodalClient;
        _client = nextClient;
        _multimodalClient = nextMultimodal;
        if (!next.CompactionModel.Enabled)
        {
            _compactor = CreateCompactor(nextClient);
        }

        if (!ReferenceEquals(previous, nextClient)
            && !ReferenceEquals(previous, _approvalClient)
            && !ReferenceEquals(previous, _compactionClient))
        {
            DisposeClient(previous);
        }

        if (!ReferenceEquals(previousMultimodal, nextMultimodal))
        {
            DisposeClient(previousMultimodal);
        }

        error = string.Empty;
        return true;
    }

    private bool RefreshSideClients(ProviderName provider, string model, out string error)
    {
        error = string.Empty;
        _clientView.ReleaseSession(_client, _multimodalClient);
        if (UsesModel(_settings.ImageModel.Provider, _settings.ImageModel.Model, provider, model))
        {
            ReleaseImageDescriptionClient();
        }

        if (_settings.ApprovalModel.Enabled
            && UsesModel(_settings.ApprovalModel.Provider, _settings.ApprovalModel.Model, provider, model))
        {
            ReleaseApprovalClient();
            _clientView.ReleaseReview();
        }

        if (!_settings.CompactionModel.Enabled
            || !UsesModel(_settings.CompactionModel.Provider, _settings.CompactionModel.Model, provider, model))
        {
            return true;
        }

        if (!TryPrepareCompactionClient(_settings.CompactionModel, out var client, out var prepareError)
            || client is null)
        {
            error = string.IsNullOrWhiteSpace(prepareError)
                ? "The compaction model could not be refreshed."
                : "The compaction model could not be refreshed: " + prepareError;
            return false;
        }

        ReplaceCompactionClient(_settings.CompactionModel, client);
        return true;
    }

    private void ReleaseImageDescriptionClient()
    {
        if (_imageDescriptionClient is null)
        {
            return;
        }

        if (!ReferenceEquals(_imageDescriptionClient, _multimodalClient))
        {
            DisposeClient(_imageDescriptionClient);
        }

        _imageDescriptionClient = null;
    }

    private static bool UsesModel(
        string? provider,
        string? model,
        ProviderName changedProvider,
        string changedModel)
    {
        if (provider is null || model is null)
        {
            return false;
        }

        return ProviderName.TryParse(provider, out var selectedProvider)
            && selectedProvider is not null
            && selectedProvider == changedProvider
            && string.Equals(model, changedModel, StringComparison.Ordinal);
    }

    private void ChangeThinking(string argument)
    {
        var model = _settings.ActiveModel;
        if (!model.Thinking)
        {
            Error("The selected model does not support thinking.");
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
                Error(exception.Message);
                return;
            }

            if (selection == ThinkingSelection.Off && !model.ThinkingCanDisable)
            {
                Error("The selected model cannot disable thinking.");
                return;
            }

            if (selection != ThinkingSelection.Default
                && selection != ThinkingSelection.Off
                && !model.AllowsEffort(selection.Value))
            {
                Error(
                    $"Thinking effort '{selection.Value}' is not available for this model.");
                return;
            }

            _thinkingEffort = selection;
        }

        SaveSettings(current => current.WithThinkingEffort(_thinkingEffort));
        RebuildExecutors();
        RefreshChrome();
        Note("Thinking  " + ThinkingLabel.For(_thinkingEffort));
    }

    private void ChangeEstimatedTokens(string argument)
    {
        bool enabled;
        if (string.IsNullOrWhiteSpace(argument))
        {
            enabled = !_settings.EstimatedTokens;
        }
        else if (!TryParseToggle(argument, out enabled))
        {
            Error("Estimated tokens is on or off.");
            return;
        }

        SaveSettings(current => current.WithEstimatedTokens(enabled));
        PublishPreferences();
        Note(
            "Estimated tokens  " + (_settings.EstimatedTokens ? "On" : "Off"));
    }

    private void ChangeVerbose(string argument)
    {
        if (!VerboseChangeArguments.TryParse(argument, out var target, out var enabled, out var error))
        {
            Error(error);
            return;
        }

        if (target is null)
        {
            Note(
                "Verbose tools  " + (_settings.VerboseTools ? "On" : "Off")
                + "  ·  Verbose commands  "
                + (_settings.VerboseCommands ? "On" : "Off")
                + "  ·  Verbose results  "
                + (_settings.VerboseResults ? "On" : "Off")
                + "  ·  Verbose calls  "
                + (_settings.VerboseCalls ? "On" : "Off")
                + "  ·  Verbose approvals  "
                + (_settings.VerboseApprovals ? "On" : "Off")
                + "  ·  Verbose review  "
                + (_settings.VerboseReview ? "On" : "Off")
                + "  ·  Verbose thinking  "
                + (_settings.VerboseThinking ? "On" : "Off"));
            return;
        }

        switch (target.Value)
        {
            case VerboseTarget.Tools:
                SetVerbose(
                    VerboseTarget.Tools,
                    enabled is bool toolsEnabled ? toolsEnabled : !_settings.VerboseTools);
                Note("Verbose tools  " + (_settings.VerboseTools ? "On" : "Off"));
                break;
            case VerboseTarget.Commands:
                SetVerbose(
                    VerboseTarget.Commands,
                    enabled is bool commandsEnabled ? commandsEnabled : !_settings.VerboseCommands);
                Note("Verbose commands  " + (_settings.VerboseCommands ? "On" : "Off"));
                break;
            case VerboseTarget.Results:
                SetVerbose(
                    VerboseTarget.Results,
                    enabled is bool resultsEnabled ? resultsEnabled : !_settings.VerboseResults);
                Note("Verbose results  " + (_settings.VerboseResults ? "On" : "Off"));
                break;
            case VerboseTarget.Calls:
                SetVerbose(
                    VerboseTarget.Calls,
                    enabled is bool callsEnabled ? callsEnabled : !_settings.VerboseCalls);
                Note("Verbose calls  " + (_settings.VerboseCalls ? "On" : "Off"));
                break;
            case VerboseTarget.Approvals:
                SetVerbose(
                    VerboseTarget.Approvals,
                    enabled is bool approvalsEnabled ? approvalsEnabled : !_settings.VerboseApprovals);
                Note("Verbose approvals  " + (_settings.VerboseApprovals ? "On" : "Off"));
                break;
            case VerboseTarget.Review:
                SetVerbose(
                    VerboseTarget.Review,
                    enabled is bool reviewEnabled ? reviewEnabled : !_settings.VerboseReview);
                Note("Verbose review  " + (_settings.VerboseReview ? "On" : "Off"));
                break;
            case VerboseTarget.Thinking:
                SetVerbose(
                    VerboseTarget.Thinking,
                    enabled is bool thinkingEnabled ? thinkingEnabled : !_settings.VerboseThinking);
                Note("Verbose thinking  " + (_settings.VerboseThinking ? "On" : "Off"));
                break;
            default:
                return;
        }
    }

    private static bool TryParseToggle(string argument, out bool enabled)
    {
        enabled = false;
        var value = argument.Trim();
        if (value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void ChangeModel(string argument)
    {
        if (_turnActive)
        {
            Error("Finish the current turn before switching models.");
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            Note(
                ModelSelection.FormatCatalog(
                    _settings.Catalog,
                    _settings.Provider,
                    _settings.Model));
            return;
        }

        if (!ModelSelection.TryResolve(
                _settings.Catalog,
                _settings.Provider,
                argument,
                out var selection,
                out var resolveError)
            || selection is null)
        {
            Error(resolveError);
            return;
        }

        if (selection.Provider == _settings.Provider
            && string.Equals(selection.Model, _settings.Model, StringComparison.Ordinal))
        {
            Note("Model  " + selection);
            return;
        }

        var nextSettings = _settings.WithSelection(selection.Provider, selection.Model);
        if (!_credentials.TryResolve(
                nextSettings.ActiveProvider,
                out _,
                out var credentialError))
        {
            Error(credentialError);
            return;
        }

        IStreamingChatClient? nextClient = null;
        IStreamingMultimodalChatClient? nextMultimodalClient;
        try
        {
            nextClient = CreateClient(nextSettings);
            nextMultimodalClient = CreateMultimodalClient(nextSettings);
            if ((_pendingImages.Count > 0 || HasReferencedImages())
                && nextMultimodalClient is null)
            {
                DisposeClient(nextClient);
                Error(
                    "The selected model cannot continue a session containing images.");
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisposeClient(nextClient);
            Error(exception.Message);
            return;
        }

        var previous = _client;
        var previousMultimodal = _multimodalClient;
        _client = nextClient;
        _multimodalClient = nextMultimodalClient;
        if (!_settings.CompactionModel.Enabled)
        {
            _compactor = CreateCompactor(nextClient);
        }
        _settings = nextSettings;
        _persisted = _persisted.WithSelection(selection.Provider, selection.Model);
        _settingsStore.Save(_persisted);
        _clientView.ReleaseSession(_client, _multimodalClient);
        if (!ReferenceEquals(previous, _approvalClient)
            && !ReferenceEquals(previous, _compactionClient))
        {
            DisposeClient(previous);
        }

        DisposeClient(previousMultimodal);
        ReplaceLiveSystem();
        RebuildExecutors();
        RefreshSlashCommands();
        RefreshChrome();
        ShowUsage(ShownUsage(), ShownCumulative());
        Note("Model  " + selection);
    }

    private void ExportSession(string argument)
    {
        if (_turnActive)
        {
            Error("Finish the current turn before exporting.");
            return;
        }

        IReadOnlyList<string> tokens;
        try
        {
            tokens = string.IsNullOrWhiteSpace(argument)
                ? []
                : CommandArguments.Split(argument);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        if (tokens.Count == 0)
        {
            WriteExportUsage();
            return;
        }

        if (!ExportSessionArguments.TryParse(tokens, out var options, out var parseError))
        {
            if (parseError.Length > 0)
            {
                Error(parseError);
                return;
            }

            WriteExportUsage();
            return;
        }

        switch (options.Format)
        {
            case "markdown":
                ExportMarkdown(options.Path, options.IncludeSystem);
                return;
            case "json":
                ExportJson(options.Path, options.IncludeSystem);
                return;
            default:
                WriteExportUsage();
                return;
        }
    }

    private void ExportMarkdown(string? explicitPath, bool includeSystem)
    {
        if (!TryResolveExportOutputPath(explicitPath, ".md", out var path, out var error))
        {
            Error(error);
            return;
        }

        var metadata = CreateExportMetadata();
        var items = TranscriptExport.ConversationItems(ArchiveSnapshot());
        var systemText = includeSystem ? CurrentSystemText() : null;
        var markdown = TranscriptExport.RenderMarkdown(
            metadata,
            items,
            _todos.Snapshot(),
            systemText);
        WriteExportFile(path, markdown);
    }

    private void ExportJson(string? explicitPath, bool includeSystem)
    {
        if (!TryResolveExportOutputPath(explicitPath, ".json", out var path, out var error))
        {
            Error(error);
            return;
        }

        var metadata = CreateExportMetadata();
        var document = CreateExportDocument();
        var systemText = includeSystem ? CurrentSystemText() : null;
        var json = SessionJsonExport.Render(metadata, document, systemText);
        WriteExportFile(path, json);
    }

    private void ExportPromptTemplates(string? explicitDirectory)
    {
        if (_turnActive)
        {
            Error("Finish the current turn before exporting prompts.");
            return;
        }

        string directory;
        if (string.IsNullOrWhiteSpace(explicitDirectory))
        {
            if (!TryResolveExportDirectory(out var exportRoot, out var resolveError))
            {
                Error(resolveError);
                return;
            }

            directory = Path.Combine(exportRoot, "prompts");
        }
        else if (!ExportPath.TryResolveOutputPath(
                     explicitDirectory,
                     _workspace.Root,
                     _home,
                     out directory,
                     out var error))
        {
            Error(error);
            return;
        }

        if (!ExportFilesystem.TryEnsureDirectory(directory, out var ensureError))
        {
            Error(ensureError);
            return;
        }

        try
        {
            PromptTemplateExport.Write(directory);
            Note("Exported prompts  " + directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error("Export failed  " + exception.Message);
        }
    }

    private void WriteExportUsage()
    {
        if (!TryResolveExportDirectory(out var directory, out var error))
        {
            Error(error);
            return;
        }

        Note(ExportText.Usage(_sessionId, directory));
    }

    private bool TryResolveExportDirectory(out string directory, out string error) =>
        ExportDirectory.TryResolve(_settings.ExportDirectory, _home, _workspace.Root, out directory, out error);

    private bool TryResolveExportOutputPath(
        string? explicitPath,
        string extension,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!TryResolveExportDirectory(out var exportRoot, out error))
            {
                return false;
            }

            if (!ExportFilesystem.TryEnsureDirectory(exportRoot, out error))
            {
                return false;
            }

            fullPath = Path.Combine(exportRoot, _sessionId + extension);
            return true;
        }

        if (!ExportPath.TryResolveOutputPath(explicitPath, _workspace.Root, _home, out fullPath, out error))
        {
            return false;
        }

        if (Directory.Exists(fullPath)
            || explicitPath.EndsWith('/')
            || explicitPath.EndsWith('\\'))
        {
            if (!ExportFilesystem.TryEnsureDirectory(fullPath, out error))
            {
                return false;
            }

            fullPath = Path.Combine(fullPath, _sessionId + extension);
        }

        return true;
    }

    private SessionExportMetadata CreateExportMetadata() =>
        new(
            _sessionId,
            _workspace.Root,
            _settings.Provider.Value,
            _settings.Model,
            _promptResolution.PromptSet,
            _planMode,
            DateTimeOffset.UtcNow);

    private SessionDocument CreateExportDocument()
    {
        var document = CreateDocument();
        document.Items = TranscriptCodec.Write(TranscriptExport.ConversationItems(ArchiveSnapshot()));
        document.UpdatedUtc = DateTimeOffset.UtcNow;
        return document;
    }
    private void WriteExportFile(string path, string contents)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)
                && !ExportFilesystem.TryEnsureDirectory(directory, out var ensureError))
            {
                Error(ensureError);
                return;
            }

            File.WriteAllText(path, contents, Encoding.UTF8);
            Note("Exported  " + path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            Error("Export failed  " + exception.Message);
        }
    }

    private void ChangePromptSet(string argument)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = string.IsNullOrWhiteSpace(argument)
                ? []
                : CommandArguments.Split(argument);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        if (tokens.Count > 0
            && string.Equals(tokens[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Count > 2)
            {
                Error("Prompt export accepts at most one directory.");
                return;
            }

            ExportPromptTemplates(tokens.Count > 1 ? tokens[1] : null);
            return;
        }

        if (tokens.Count == 0)
        {
            Note(PromptSelectionText.Format(_promptResolution));
            return;
        }

        if (_turnActive)
        {
            Error("Finish the current turn before switching prompt sets.");
            return;
        }

        if (_settings.PromptSetOverride is not null)
        {
            Error(
                "Prompt set "
                + _settings.PromptSetOverride
                + " is forced for this process.");
            return;
        }

        if (!PromptSetChangeArguments.TryParseName(tokens, out var requested, out var parseError))
        {
            Error(parseError);
            return;
        }
        if (string.Equals(requested, PromptSetNames.Default, StringComparison.Ordinal))
        {
            if (!PromptSetInventory.TryDisableAll(_home, out var disableError))
            {
                Error(disableError);
                return;
            }
        }
        else if (!_promptStore.ContainsSet(requested))
        {
            Error("Prompt set not found  " + requested);
            return;
        }
        else if (!PromptSetInventory.TryEnable(_home, requested, out var enableError))
        {
            Error(enableError);
            return;
        }

        var resolution = ResolvePrompts();
        _promptResolution = resolution;
        _prompts = resolution.Prompts;
        ReplaceLiveSystem();
        RebuildExecutors();
        RefreshSlashCommands();
        RefreshChrome();
        WritePromptNotes();
        Note("Prompt set  " + resolution.PromptSet);
    }

    private void ChangePromptAttachments(string argument)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = string.IsNullOrWhiteSpace(argument)
                ? []
                : CommandArguments.Split(argument);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        if (tokens.Count == 0)
        {
            Note(PromptAttachmentText.Format(_promptResolution));
            return;
        }

        if (_turnActive)
        {
            Error("Finish the current turn before changing prompt attachments.");
            return;
        }

        if (!_settings.UsePromptAttachments)
        {
            Error("Prompt attachments are off for this process.");
            return;
        }

        if (!PromptAttachmentChangeArguments.TryParse(tokens, out var action, out var name, out var parseError))
        {
            Error(parseError);
            return;
        }

        if (IsForcedAttachment(name))
        {
            Error($"Prompt attachment '{name}' is forced for this process.");
            return;
        }

        var changed = false;
        if (action == "enable")
        {
            if (!AttachmentAvailable(name))
            {
                parseError = "Prompt attachment not found  " + name;
            }
            else if (AttachmentEnabled(name))
            {
                parseError = $"Prompt attachment '{name}' is already enabled.";
            }
            else
            {
                changed = PromptAttachmentInventory.TrySetEnabled(
                    _home,
                    _workspace.Root,
                    name,
                    source: null,
                    enabled: true,
                    out parseError);
            }
        }
        else if (action == "disable")
        {
            if (!AttachmentEnabled(name))
            {
                parseError = $"Prompt attachment '{name}' is not enabled.";
            }
            else
            {
                changed = PromptAttachmentInventory.TrySetEnabled(
                    _home,
                    _workspace.Root,
                    name,
                    source: null,
                    enabled: false,
                    out parseError);
            }
        }
        else if (action == "up")
        {
            changed = PromptAttachmentInventory.TryMove(
                _home,
                _workspace.Root,
                name,
                earlier: true,
                out parseError);
        }
        else if (action == "down")
        {
            changed = PromptAttachmentInventory.TryMove(
                _home,
                _workspace.Root,
                name,
                earlier: false,
                out parseError);
        }
        else
        {
            parseError = "Prompt attachments accept enable, disable, up, or down, and one name.";
        }

        if (!changed)
        {
            Error(parseError);
            return;
        }

        var resolution = ResolvePrompts();
        _promptResolution = resolution;
        _prompts = resolution.Prompts;
        ReplaceLiveSystem();
        RebuildExecutors();
        RefreshSlashCommands();
        RefreshChrome();
        WritePromptNotes();
        var status = PromptAttachmentText.Status(resolution);
        Note(status.Length == 0 ? "Prompt attachments  none" : "Prompt attachments  " + status);
    }

    private bool AttachmentAvailable(string name)
    {
        foreach (var entry in _promptResolution.Attachments)
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool AttachmentEnabled(string name)
    {
        foreach (var entry in _promptResolution.Attachments)
        {
            if (entry.Enabled && string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ChangeTools(string argument)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            ShowTools();
            return;
        }

        var command = parts[0].ToLowerInvariant();
        if (command == "approval" && parts.Length == 1)
        {
            Note(ToolListText.FormatApproval(_settings.ExternalToolApproval));
            return;
        }

        if (command is "home" or "project")
        {
            if (CatalogCommand.LooksLike(parts))
            {
                ChangeToolSet(parts);
                return;
            }

            ChangeToolApproval(command, parts);
            return;
        }

        if (CatalogCommand.LooksLike(parts))
        {
            ChangeToolSet(parts);
            return;
        }

        if (command is "on" or "off" or "reload")
        {
            if (parts.Length != 1)
            {
                WriteToolsUsage();
                return;
            }

            ChangeToolLoading(command);
            return;
        }

        WriteToolsUsage();
    }

    private void AttachImage(string argument)
    {
        if (_multimodalClient is null)
        {
            Error(
                "The selected model and provider do not support image input.");
            return;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = string.IsNullOrWhiteSpace(argument)
                ? []
                : CommandArguments.Split(argument);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        if (paths.Count != 1)
        {
            Error("Usage: /attach <workspace-image-path>");
            return;
        }

        if (!_workspace.TryResolveExistingFile(paths[0], out var path, out var error))
        {
            Error(error);
            return;
        }

        try
        {
            var image = ImageFile.Load(path, ReserveImageNumber());
            AddImage(image);
            _pendingImages.Add(image.Number);
            Note($"Attached  {image.Marker}  {Path.GetFileName(path)}");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException)
        {
            Error("Image attachment failed  " + exception.Message);
        }
    }

    /// <summary>
    /// Reads an image from the system clipboard, attaches it to the draft, and
    /// returns its trusted marker, or null when no image was attached.
    /// </summary>
    public async Task<string?> PasteClipboardImageAsync(
        CancellationToken cancellationToken)
    {
        if (_multimodalClient is null)
        {
            Error(
                "The selected model and provider do not support image input.");
            return null;
        }

        var (image, error) = await ClipboardImageReader.TryReadAsync(
            ReserveImageNumber(),
            cancellationToken);
        if (image is null)
        {
            Error(error);
            return null;
        }

        AddImage(image);
        _draftImages.Add(image.Number);
        return image.Marker;
    }

    private void ChangeToolApproval(string source, IReadOnlyList<string> parts)
    {
        if (parts.Count == 1)
        {
            var currentPolicy = source == "home"
                ? _settings.ExternalToolApproval.Home
                : _settings.ExternalToolApproval.Project;
            Note($"Tool approval  {Title(source)} {Title(currentPolicy.Value)}");
            return;
        }

        if (parts.Count != 2)
        {
            WriteToolsUsage();
            return;
        }

        ExternalToolTrustPolicy policy;
        try
        {
            policy = ExternalToolTrustPolicy.Parse(parts[1]);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        if (_turnActive)
        {
            Error("Finish the current turn before changing tool approval.");
            return;
        }

        var approval = source == "home"
            ? _settings.ExternalToolApproval.WithHome(policy)
            : _settings.ExternalToolApproval.WithProject(policy);
        SaveSettings(current => current.WithExternalToolApproval(approval));
        ReloadExternalToolsWithProgress();
        PublishPluginEnvironment();
        RebuildExecutors();
        Note($"Tool approval  {Title(source)} {Title(policy.Value)}");
    }

    private void ChangeToolLoading(string command)
    {
        if (_turnActive)
        {
            Error("Finish the current turn before reloading tools.");
            return;
        }

        if (command != "reload")
        {
            SaveSettings(current => current.WithExternalTools(command == "on"));
        }

        ReloadExternalToolsWithProgress();
        PublishPluginEnvironment();
        RebuildExecutors();
        ReplaceLiveSystem();
        WriteExternalNotes();
        Note(command == "reload" ? "Tools reloaded" : "External tools  " + Title(command));
    }

    private void ShowTools()
    {
        var planDefinitions = _multimodalClient is null
            ? _planExecutor.Definitions
            : _planMultimodalExecutor.Definitions;
        var workDefinitions = _multimodalClient is null
            ? _workExecutor.Definitions
            : _workMultimodalExecutor.Definitions;
        Publish(new ToolsListed(planDefinitions, workDefinitions, _external, _settings));
    }

    private void ShowStatus(string argument)
    {
        var full = string.Equals(argument, "full", StringComparison.OrdinalIgnoreCase);
        if (argument.Length > 0 && !full)
        {
            Error("Status command must be /status or /status full.");
            return;
        }

        var planToolCount = _multimodalClient is null
            ? _planExecutor.Definitions.Count
            : _planMultimodalExecutor.Definitions.Count;
        var workToolCount = _multimodalClient is null
            ? _workExecutor.Definitions.Count
            : _workMultimodalExecutor.Definitions.Count;
        lock (_usageGate)
        {
            _shownUsage = _ledger.Usage;
            _shownCumulative = _ledger.CumulativeUsage;
        }

        Publish(new StatusReported(
            new SessionStatus(
                SessionId: _sessionId,
                StartedUtc: _sessionCreatedUtc,
                WorkspaceRoot: _workspace.Root,
                PlanMode: _planMode,
                Approval: _approval,
                Thinking: CurrentThinkingStatus(),
                PromptSet: _promptResolution.PromptSet,
                Provider: _settings.Provider.Value,
                Model: _settings.Model,
                ContextWindow: _settings.ContextWindow,
                Usage: _ledger.Usage,
                UserTurns: _ledger.UserTurns,
                ModelCalls: _ledger.ModelCalls,
                ToolCalls: _ledger.ToolCalls,
                QueuedMessages: _queue.Count + _deferredStops.Count,
                Todos: _todos.Count,
                SkillsEnabled: _settings.Skills,
                ExternalToolsEnabled: _settings.ExternalTools,
                EstimatedTokensEnabled: _settings.EstimatedTokens,
                VerboseToolsEnabled: _settings.VerboseTools,
                VerboseCommandsEnabled: _settings.VerboseCommands,
                VerboseResultsEnabled: _settings.VerboseResults,
                VerboseCallsEnabled: _settings.VerboseCalls,
                VerboseApprovalsEnabled: _settings.VerboseApprovals,
                VerboseThinkingEnabled: _settings.VerboseThinking,
                VerboseReviewEnabled: _settings.VerboseReview,
                PlanTools: planToolCount,
                WorkTools: workToolCount,
                ExternalTools: _external.Tools.Count,
                PluginsEnabled: _settings.Plugins,
                Plugins: _loadedPlugins.Plugins.Count,
                CumulativeUsage: _ledger.CumulativeUsage,
                CustomStatusLineEnabled: _settings.StatusLine.Enabled,
                ApprovalModel: _settings.ApprovalModel.Enabled
                    ? _settings.ApprovalModel.Provider + " / " + _settings.ApprovalModel.Model
                    : null,
                ApprovalThinking: _settings.ApprovalModel.Enabled
                    ? CurrentApprovalThinkingStatus()
                    : null,
                Retry: DescribeRetry(_settings.Retry),
                ApprovalRetry: DescribeRetry(_settings.ApprovalRetry),
                CompactionModel: CompactionModelStatus(),
                MicroCompaction: DescribeMicroCompaction(_settings.MicroCompaction),
                GlobalRulesEnabled: _settings.RuleFiles.Global,
                ProjectRulesEnabled: _settings.RuleFiles.Project,
                ServiceTier: CatalogSlotStatus.For("Tier", _settings.ServiceTier),
                Speed: CatalogSlotStatus.For("Speed", _settings.Speed)),
            full));
    }

    private static string DescribeRetry(RetrySettings settings)
    {
        if (!settings.Enabled)
        {
            return "Disabled";
        }

        var retries = settings.MaximumRetries == 1 ? "retry" : "retries";
        var initial = settings.InitialDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var factor = settings.BackoffFactor.ToString("0.###", CultureInfo.InvariantCulture);
        var jitter = (settings.JitterFactor * 100).ToString("0.###", CultureInfo.InvariantCulture);
        var cap = settings.MaximumDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"{settings.MaximumRetries} {retries}, {initial}s base x{factor}, {jitter}% jitter, {cap}s cap";
    }

    private static string? DescribeMicroCompaction(MicroCompactionSettings settings)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        var turns = settings.ProtectTurns == 1 ? "turn" : "turns";
        return "On  "
            + settings.ProtectTurns
            + " "
            + turns
            + ", protect "
            + settings.ProtectTokens
            + ", minimum "
            + settings.MinimumTokens;
    }

    private void ChangeStatusLine(string argument)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = string.IsNullOrWhiteSpace(argument) ? [] : CommandArguments.Split(argument);
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        if (tokens.Count == 0)
        {
            Note(DescribeStatusLine());
            return;
        }

        StatusLineSettings statusLine;
        try
        {
            statusLine = tokens[0].ToLowerInvariant() switch
            {
                "on" when tokens.Count == 1 => _settings.StatusLine.WithEnabled(true),
                "off" when tokens.Count == 1 => _settings.StatusLine.WithEnabled(false),
                "reset" when tokens.Count == 1 => new StatusLineSettings(true),
                "on" or "off" or "reset" => throw new ArgumentException(
                    "Status line on, off, and reset do not accept additional fields."),
                _ => _settings.StatusLine.WithFields(tokens, _loadedPlugins.StatusFieldNames)
            };
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        SaveSettings(current => current.WithStatusLine(statusLine));
        PublishPreferences();
        Note("Custom status line  " + (statusLine.Enabled ? "On" : "Off"));
    }

    private string DescribeStatusLine()
    {
        var text = new StringBuilder();
        text.Append("Custom status line  ");
        text.Append(_settings.StatusLine.Enabled ? "On" : "Off");
        text.Append("\nFields  ");
        text.Append(string.Join(' ', _settings.StatusLine.Fields));
        text.Append("\nAvailable  ");
        text.Append(string.Join(' ', StatusLineSettings.AvailableFields));
        if (_loadedPlugins.StatusFields.Count > 0)
        {
            text.Append("\nPlugins");
            foreach (var field in _loadedPlugins.StatusFields)
            {
                text.Append('\n');
                text.Append(field.Name);
                text.Append("  ");
                text.Append(field.DirectoryName);
                if (!string.IsNullOrWhiteSpace(field.Description))
                {
                    text.Append("  ");
                    text.Append(field.Description);
                }
            }
        }

        var unavailable = new List<string>();
        foreach (var field in _settings.StatusLine.Fields)
        {
            if (!StatusLineSettings.AvailableFields.Contains(field, StringComparer.Ordinal)
                && !_loadedPlugins.StatusFieldNames.Contains(field))
            {
                unavailable.Add(field);
            }
        }

        if (unavailable.Count > 0)
        {
            text.Append("\nUnavailable  ");
            text.Append(string.Join(' ', unavailable));
        }

        return text.ToString();
    }

    private void WriteToolsUsage()
    {
        Error(
            "Tools command must be /tools, /tools approval, /tools on|off|reload, "
            + "/tools enable|disable|show <directory>, "
            + "/tools home|project enable|disable|show <directory>, "
            + "/tools home author|host, or /tools project author|host.");
    }

    private void ChangeToolSet(IReadOnlyList<string> parts)
    {
        if (!CatalogCommand.TryParse(parts, out var command, out var error) || command is null)
        {
            Error(error);
            return;
        }

        if (_turnActive && command.Verb != "show")
        {
            Error("Finish the current turn before reloading tools.");
            return;
        }

        var source = command.Source switch
        {
            "home" => ExternalToolSource.Home,
            "project" => ExternalToolSource.Project,
            _ => (ExternalToolSource?)null
        };
        if (command.Verb == "show")
        {
            if (!ToolSetInventory.TryFind(_home, _workspace.Root, command.DirectoryName, source, out var entry, out error)
                || entry is null)
            {
                Error(error);
                return;
            }

            foreach (var line in ToolSetInventory.Format(entry))
            {
                Note(line);
            }

            if (!_settings.ExternalTools)
            {
                Note(ToolSetInventory.DiscoveryOff);
            }

            return;
        }

        var enabled = command.Verb == "enable";
        if (!ToolSetInventory.TrySetEnabled(
                _home,
                _workspace.Root,
                command.DirectoryName,
                source,
                enabled,
                out var updated,
                out var changed,
                out error)
            || updated is null)
        {
            Error(error);
            return;
        }

        if (changed)
        {
            ReloadExternalToolsWithProgress();
            PublishPluginEnvironment();
            RebuildExecutors();
            ReplaceLiveSystem();
            WriteExternalNotes();
        }

        Note(changed
            ? $"Tool set {updated.DirectoryName}  {(enabled ? "Enabled" : "Disabled")}"
            : $"Tool set {updated.DirectoryName} is already {(enabled ? "enabled" : "disabled")}.");
        if (enabled && !_settings.ExternalTools)
        {
            Note(ToolSetInventory.DiscoveryOff);
        }
    }

    private static string Title(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private void ReplaceClients()
    {
        var next = CreateClient(_settings);
        IStreamingMultimodalChatClient? multimodal;
        try
        {
            multimodal = CreateMultimodalClient(_settings);
        }
        catch
        {
            DisposeClient(next);
            throw;
        }

        DisposeClient();
        _client = next;
        _multimodalClient = multimodal;
        BindCompactor();
    }

    private PluginModel CurrentPluginModel() =>
        PluginModelFacts.Describe(
            _settings.ActiveProvider,
            _settings.Model,
            _settings.ActiveModel,
            _thinkingEffort.Value,
            _settings.ServiceTier,
            _settings.Speed);

    private PluginReview CurrentPluginReview() =>
        PluginModelFacts.DescribeReview(
            _settings.ApprovalModel,
            _settings.Catalog,
            _settings);

    /// <summary>
    /// A new client on the session model. Plugin calls stay off the instance
    /// that is streaming the current turn.
    /// </summary>
    private IStreamingChatClient CreateSideSessionClient() => CreateClient(_settings);

    private IStreamingMultimodalChatClient? CreateSideImageClient() =>
        CreateMultimodalClient(_settings);

    private ImageDescriber? CreateImageDescriber()
    {
        if (!_settings.ImageModel.IsConfigured)
        {
            return null;
        }

        var selected = _settings.WithSelection(
            new ProviderName(_settings.ImageModel.Provider!),
            _settings.ImageModel.Model!);
        return new ImageDescriber(
            OpenImageDescriptionClient,
            ImageDescriptionSystemText(selected),
            _settings.ImageModel.ThinkingEffort.ToReasoningOptions(selected.ActiveModel),
            _prompts.ImageUser,
            _placeholders);
    }

    private string ImageDescriptionSystemText(HarnessSettings selected) =>
        ImageDescriptionPrompt.ComposeSystem(
            PromptContext.Create(
                _workspace.Root,
                selected.Provider.Value,
                selected.Model,
                "image",
                skills: string.Empty,
                instructions: string.Empty,
                sessionId: _sessionId ?? string.Empty,
                approval: _approval.Value,
                directories: DirectoryAccessText.Render(_workspace.Access.Snapshot())),
            _placeholders,
            _prompts.ImageSystem);

    private IStreamingMultimodalChatClient OpenImageDescriptionClient()
    {
        if (_imageDescriptionClient is not null)
        {
            return _imageDescriptionClient;
        }

        var selected = _settings.WithSelection(
            new ProviderName(_settings.ImageModel.Provider!),
            _settings.ImageModel.Model!);
        var client = CreateMultimodalClient(selected)
            ?? throw new InvalidOperationException(
                "The image model does not accept image input.");
        _imageDescriptionClient = client;
        return client;
    }

    private IStreamingChatClient CreateSideReviewClient()
    {
        var approval = _settings.ApprovalModel;
        if (!approval.Enabled || approval.Provider is null || approval.Model is null)
        {
            throw new InvalidOperationException("Review is using the session model.");
        }

        var selected = _settings.WithSelection(
            new ProviderName(approval.Provider),
            approval.Model);
        return CreateClient(selected);
    }

    private IStreamingChatClient CreateClient(HarnessSettings settings)
    {
        if (!_credentials.TryResolve(settings.ActiveProvider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error);
        }

        try
        {
            return ChatClientFactory.Create(settings, apiKey, _plugins);
        }
        catch (NotSupportedException) when (
            _loadedPlugins.TryCreateClient(settings, apiKey, out var client) && client is not null)
        {
            return client;
        }
    }

    private IStreamingMultimodalChatClient? CreateMultimodalClient(
        HarnessSettings settings)
    {
        if (!_credentials.TryResolve(settings.ActiveProvider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (!settings.ActiveModel.ImageInput)
        {
            return null;
        }

        var protocol = settings.ActiveProvider.Protocol;
        if (_plugins.Clients.Any(factory => factory.CanCreate(protocol)))
        {
            return MultimodalChatClientFactory.Create(settings, apiKey, _plugins);
        }

        return _loadedPlugins.CreateMultimodal(settings, apiKey);
    }

    private void DisposeClient()
    {
        _clientView.ReleaseSession();
        _clientView.ReleaseReview();
        ReleaseApprovalClient();
        ReleaseCompactionClient();
        DisposeClient(_client);
        DisposeClient(_multimodalClient);
        DisposeClient(_imageDescriptionClient);
        _imageDescriptionClient = null;
    }

    private static void DisposeClient(IStreamingChatClient? client)
    {
        (client as IDisposable)?.Dispose();
    }

    private static void DisposeClient(IStreamingMultimodalChatClient? client)
    {
        (client as IDisposable)?.Dispose();
    }

    private async Task ChangeDirectoryAsync(string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            Note(_workspace.Root);
            return;
        }

        if (WorkspaceChangeBlocked())
        {
            return;
        }

        if (!_workspace.TryResolve(argument, out var candidate, out var error))
        {
            Error(error);
            return;
        }

        await EnterWorkspaceAsync(candidate, cancellationToken);
    }

    private async Task ChangeSpaceAsync(string argument, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            Error("Space command must be /space.");
            return;
        }

        if (WorkspaceChangeBlocked())
        {
            return;
        }

        string space;
        try
        {
            space = OperatorSpace.EnsureCreated(_home);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error("Operator space could not be created.");
            return;
        }

        if (string.Equals(space, _workspace.Root, StringComparison.Ordinal))
        {
            Note("Workspace  " + _workspace.Root);
            return;
        }

        await EnterWorkspaceAsync(space, cancellationToken);
    }

    private bool WorkspaceChangeBlocked()
    {
        if (!_turnActive && _compactSource is null)
        {
            return false;
        }

        Error("Finish the current turn before changing workspace.");
        return true;
    }

    /// <summary>
    /// Switches to <paramref name="candidate"/> with the same trust and reload
    /// path as <c>/cd</c>. The current workspace returns without a reload.
    /// </summary>
    private async Task<bool> EnterWorkspaceAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        if (string.Equals(candidate, _workspace.Root, StringComparison.Ordinal))
        {
            return true;
        }

        if (!await ConfirmWorkspaceAsync(candidate, cancellationToken))
        {
            Note("Staying in " + _workspace.Root);
            return false;
        }

        await RebindPluginSessionAsync(
            () =>
            {
                ApplyWorkspace(candidate);
                SeedWorkspaceDefaults();
            },
            cancellationToken);
        SaveSession();
        RefreshChrome();
        Note("Workspace  " + _workspace.Root);
        return true;
    }

    /// <summary>
    /// Reloads workspace-owned catalogs after the root has changed.
    /// The caller ends the plugin session before this and starts it after.
    /// </summary>
    private void ApplyWorkspace(string candidate)
    {
        _workspace.Access.Clear();
        _workspace.SetRoot(candidate);
        ReloadSkills();
        ReloadPluginsWithProgress();
        ReloadExternalToolsWithProgress();
        PublishPluginEnvironment();
        ReloadPrompts();
        RebuildExecutors();
        WritePluginNotes();
        WriteExternalNotes();
    }

    private async Task<bool> ChangeTrustAsync(string argument, CancellationToken cancellationToken)
    {
        var command = argument.Trim().ToLowerInvariant();
        var trustRoot = _trust.TrustRoot(_workspace.Root);
        if (command.Length == 0)
        {
            Note("Workspace trust  " + (_settings.WorkspaceTrust ? "on" : "off"));
            Note("Trust root  " + trustRoot);
            Note("Trusted  " + (_trust.Contains(_workspace.Root) ? "yes" : "no"));
            return false;
        }

        if (command is not ("on" or "off" or "forget"))
        {
            Error("Trust command must be /trust, /trust on|off, or /trust forget.");
            return false;
        }

        if (command == "forget")
        {
            if (OperatorSpace.Is(_home, trustRoot))
            {
                Note("The operator space stays trusted.");
                return false;
            }

            _trust.Forget(_workspace.Root);
            Note("Forgot  " + trustRoot);
            return false;
        }

        var enabled = command == "on";
        SaveSettings(current => current.WithWorkspaceTrust(enabled));
        Note("Workspace trust  " + (enabled ? "on" : "off"));
        if (!enabled || _trust.Contains(_workspace.Root))
        {
            return false;
        }

        var accepted = await _frontEnd.Trust.ConfirmAsync(
            new WorkspaceTrustRequest(_workspace.Root, trustRoot),
            cancellationToken);
        if (!accepted)
        {
            return true;
        }

        _trust.Remember(_workspace.Root);
        Note("Trusted  " + trustRoot);
        return false;
    }

    /// <summary>
    /// Returns false when the operator declines, without recording that decline.
    /// </summary>
    private async Task<bool> ConfirmWorkspaceAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (!_settings.WorkspaceTrust)
        {
            return true;
        }

        var trustRoot = _trust.TrustRoot(workspaceRoot);
        if (_trust.Contains(workspaceRoot))
        {
            return true;
        }

        var accepted = await _frontEnd.Trust.ConfirmAsync(
            new WorkspaceTrustRequest(workspaceRoot, trustRoot),
            cancellationToken);
        if (!accepted)
        {
            return false;
        }

        _trust.Remember(workspaceRoot);
        return true;
    }

    private void ChangeRules(string argument)
    {
        if (!RuleFileArguments.TryParse(argument, out var change, out var error))
        {
            Error(error);
            return;
        }

        if (change is null)
        {
            Note(RuleFileArguments.Format(_settings.RuleFiles));
            return;
        }

        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before changing rule files.");
            return;
        }

        SaveSettings(current => current.WithRuleFiles(change.Apply(current.RuleFiles)));
        ReloadPrompts();
        Note(RuleFileArguments.Format(_settings.RuleFiles));
    }

    private PromptContext CurrentPromptContext()
    {
        var directories = DirectoryAccessText.Render(_workspace.Access.Snapshot());
        return PromptContext.Create(
            _workspace.Root,
            _settings.Provider.Value,
            _settings.Model,
            _planMode ? "plan" : "work",
            _skills is null ? string.Empty : SkillGuidance.Render(_skills),
            _prompts.Instructions + (directories.Length == 0 ? string.Empty : "\n\n" + directories),
            sessionId: _sessionId,
            approval: _approval.Value,
            directories: directories);
    }

    private string CurrentSystemText()
    {
        var text = ApplyPluginPrompt(
            _planMode
                ? _prompts.ComposePlan(CurrentPromptContext(), _placeholders)
                : _prompts.ComposeWork(CurrentPromptContext(), _placeholders),
            _planMode ? "plan" : "work");
        var disabled = BuiltInToolSelection.ModelText(_settings.DisabledBuiltInTools);
        if (disabled.Length == 0)
        {
            return text;
        }

        return text.Length == 0 ? disabled : text + "\n\n" + disabled;
    }

    private string CurrentReviewSystemText() =>
        ApplyPluginPrompt(
            _prompts.ComposeReview(CurrentPromptContext().WithMode("review"), _placeholders),
            "review");

    private PromptBinding CurrentPromptBinding(PromptContext context) =>
        new(context, Placeholders: _placeholders);

    private string ApplyPluginPrompt(string text, string mode) =>
        _hooks.FinishPrompt(mode, _prompts.Instructions, text);

    private void ReloadPrompts()
    {
        _promptResolution = ResolvePrompts();
        _prompts = _promptResolution.Prompts;
        ReplaceLiveSystem();
        WritePromptNotes();
    }

    private PromptResolution ResolvePrompts() =>
        _promptStore.Resolve(
            _workspace.Root,
            _settings.PromptSetOverride,
            _settings.UsePromptAttachments,
            _settings.PromptAttachments,
            _settings.RuleFiles);

    private bool IsForcedAttachment(string name)
    {
        foreach (var forced in _settings.PromptAttachments)
        {
            if (string.Equals(forced, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private CompactionLimits CurrentLimits() =>
        new(
            _settings.ContextWindow,
            _settings.ActiveModel.MaxTokens,
            _settings.CompactionThreshold,
            summaryWindow: CompactionSummaryWindow());

    private async Task CompactForcedAsync(CancellationToken cancellationToken)
    {
        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before compacting.");
            return;
        }

        await RunCompactionAsync(
            _transcript,
            silentSkip: false,
            cancellationToken,
            force: true);
    }

    private async Task CompactIfNeededAsync(TurnResult result, CancellationToken cancellationToken)
    {
        var changed = TryMicrocompact(_transcript, out _);
        var usage = changed ? null : result.Usage ?? _ledger.Usage;
        if (!NeedsCompaction(_transcript, usage))
        {
            return;
        }

        await RunCompactionAsync(
            _transcript,
            silentSkip: true,
            cancellationToken);
    }

    private async Task<CompactionOutcome> CompactRoundAsync(
        IReadOnlyList<ChatItem> transcript,
        CancellationToken cancellationToken)
    {
        // Stay on the turn's list. A pruned copy would miss assistant and tool
        // messages added later in this round, and returning that same list
        // would let the turn clear the source it then copies from.
        _reviewContext.Conversation = transcript;
        var changed = TryMicrocompact(transcript, out var current);
        if (!NeedsCompaction(current, changed ? null : ShownUsage()))
        {
            return changed
                ? new CompactionOutcome([.. current], CompactionKind.Applied)
                : new CompactionOutcome(transcript, CompactionKind.Unchanged);
        }

        var outcome = await RunCompactionAsync(
            current,
            silentSkip: true,
            cancellationToken);
        if (outcome.Kind == CompactionKind.Unchanged)
        {
            Error("Session is too large to compact.");
            return new CompactionOutcome(transcript, CompactionKind.Exhausted);
        }

        return outcome;
    }

    private bool NeedsCompaction(IReadOnlyList<ChatItem> transcript, TokenUsage? reportedUsage)
    {
        var limits = CurrentLimits();
        return ContextAccountant.ShouldCompact(
            TokenEstimator.Items(transcript),
            reportedUsage,
            limits.ContextWindow,
            limits.Threshold,
            limits.MaxTokens);
    }

    private async Task<CompactionOutcome> RunCompactionAsync(
        IReadOnlyList<ChatItem> transcript,
        bool silentSkip,
        CancellationToken cancellationToken,
        bool force = false)
    {
        CompactionOutcome outcome;
        var compactSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _compactSource = compactSource;
        _compacting = true;
        try
        {
            Note("Compacting context...");
            SetActivity(SessionActivity.Compacting);
            EnsureCompactionClient();
            outcome = await _compactor.CompactAsync(
                transcript,
                _todos.Format(),
                CurrentLimits(),
                compactSource.Token,
                force);
        }
        finally
        {
            if (ReferenceEquals(_compactSource, compactSource))
            {
                _compactSource = null;
            }

            SetActivity(_turnActive ? SessionActivity.WaitingForModel : SessionActivity.Idle);
            // A canceled model call can resume inside Cancel(). Disposing the
            // source there deadlocks: Dispose waits for Cancel to finish.
            await Task.Yield();
            compactSource.Dispose();
            _compacting = false;
        }

        if (outcome.Kind == CompactionKind.Applied)
        {
            if (ReferenceEquals(transcript, _transcript))
            {
                _transcript = [.. outcome.Transcript];
                BindReviewConversation();
                RememberCompactedUsage();
                SaveSession();
            }
            else
            {
                var input = TokenEstimator.Items(outcome.Transcript);
                ShowUsage(new TokenUsage(input, 0), ShownCumulative());
            }

            Note("Compacted context");
            if (ShouldPrintSummary(force) && !string.IsNullOrWhiteSpace(outcome.Summary))
            {
                Note(outcome.Summary);
            }

            return outcome;
        }

        if (outcome.Kind == CompactionKind.Exhausted)
        {
            Error("Session is too large to compact.");
            return outcome;
        }

        if (!silentSkip)
        {
            Note("Nothing earlier to compact");
        }

        return outcome;
    }

    private bool ShouldPrintSummary(bool forced) =>
        forced
            ? _settings.ShowCompactionSummary
            : _settings.ShowAutomaticCompactionSummary;

    private void SaveSession()
    {
        _sessionStore.Save(CreateDocument());
    }

    private SessionDocument CreateDocument()
    {
        var (archiveSnapshot, archiveCount) = ArchiveSnapshotWithCount();
        var referencedMarkers = ReferencedImageMarkers(archiveSnapshot);
        if (ArchiveCount() != archiveCount)
        {
            archiveSnapshot = ArchiveSnapshot();
            referencedMarkers = ReferencedImageMarkers(archiveSnapshot);
        }

        return new SessionDocument
        {
            Id = _sessionId,
            Workspace = _workspace.Root,
            PlanMode = _planMode,
            Access = _workspace.Access.Snapshot(),
            CreatedUtc = _sessionCreatedUtc,
            Items = TranscriptCodec.Write(_transcript),
            Archive = TranscriptCodec.Write(archiveSnapshot),
            ImageMarkersTagged = true,
            Images =
            [
                .. SessionMapper.WriteImages(
                    ImageSnapshot().Values.Where(image => referencedMarkers.Contains(image.TrustedMarker))),
                .. _unavailableImages.Where(image => referencedMarkers.Contains(ImageMarkerText.Tag(image.Number)))
            ],
            Todos = SessionMapper.WriteTodos(_todos.Snapshot()),
            UserTurns = _ledger.UserTurns,
            ModelCalls = _ledger.ModelCalls,
            ToolCalls = _ledger.ToolCalls,
            Usage = SessionMapper.WriteUsage(_ledger.Usage),
            CumulativeUsage = SessionMapper.WriteUsage(_ledger.CumulativeUsage)
        };
    }

    private void BeginNewSession()
    {
        _workspace.Access.Clear();
        DiscardQueue();
        CancelAndClearSide(announce: true);
        Publish(new ImageHistoryInvalidated());
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
        _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
        lock (_archiveGate)
        {
            _archive = [];
        }

        lock (_imagesGate)
        {
            _images.Clear();
            _nextImageNumber = 1;
        }
        _unavailableImages.Clear();
        _pendingImages.Clear();
        _draftImages.Clear();
        _ledger.Clear();
        _todos.Clear();
        BindReviewConversation();
        SeedWorkspaceDefaults();
        SaveSession();
        ShowTodos();
    }

    private async Task ResumeSessionAsync(
        string argument,
        CancellationToken cancellationToken)
    {
        if (!ResumeRequest.TryParse(
                argument,
                _workspace.Root,
                _sessionStore,
                out var request,
                out var error))
        {
            Error(error);
            return;
        }

        switch (request.Target)
        {
            case ResumeRequest.Kind.Session:
                await ResumeLoadedAsync(request.Value, cancellationToken);
                return;
            case ResumeRequest.Kind.CurrentWorkspace:
                await ResumeListedAsync(
                    _workspace.Root,
                    listWorkspace: false,
                    "No session for this workspace",
                    adoptWorkspace: false,
                    cancellationToken);
                return;
            case ResumeRequest.Kind.Workspace:
                await ResumeNamedWorkspaceAsync(request.Value!, cancellationToken);
                return;
            case ResumeRequest.Kind.AllWorkspaces:
                await ResumeListedAsync(
                    workspaceRoot: null,
                    listWorkspace: true,
                    "No sessions",
                    adoptWorkspace: true,
                    cancellationToken);
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(argument),
                    request.Target,
                    "Resume target is not supported.");
        }
    }

    private async Task ResumeNamedWorkspaceAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (HasConversation())
        {
            SaveSession();
        }

        if (!await ConfirmWorkspaceAsync(workspaceRoot, cancellationToken))
        {
            Note("Staying in " + _workspace.Root);
            return;
        }

        var available = _sessionStore.List(workspaceRoot);
        if (available.Count == 0)
        {
            Error("No session for this workspace");
            return;
        }

        var chosen = await _frontEnd.Sessions.ChooseAsync(
            available,
            _sessionId,
            listWorkspace: false,
            cancellationToken);
        if (chosen is null)
        {
            return;
        }

        if (!SessionResume.TryLoad(
                _sessionStore,
                workspaceRoot,
                chosen,
                out var document,
                out var error))
        {
            Error(error);
            return;
        }

        await EnterWorkspaceAndResumeAsync(workspaceRoot, document, cancellationToken);
    }

    private async Task ResumeListedAsync(
        string? workspaceRoot,
        bool listWorkspace,
        string emptyMessage,
        bool adoptWorkspace,
        CancellationToken cancellationToken)
    {
        if (HasConversation())
        {
            SaveSession();
        }

        var available = _sessionStore.List(workspaceRoot);
        if (available.Count == 0)
        {
            Error(emptyMessage);
            return;
        }

        var chosen = await _frontEnd.Sessions.ChooseAsync(
            available,
            _sessionId,
            listWorkspace,
            cancellationToken);
        if (chosen is null)
        {
            return;
        }

        if (!SessionResume.TryLoad(
                _sessionStore,
                _workspace.Root,
                chosen,
                out var document,
                out var error))
        {
            Error(error);
            return;
        }

        if (adoptWorkspace)
        {
            if (string.IsNullOrWhiteSpace(document.Workspace)
                || !_workspace.TryResolve(document.Workspace, out var candidate, out _))
            {
                Error("Session workspace is not a directory.");
                return;
            }

            await EnterWorkspaceAndResumeAsync(candidate, document, cancellationToken);
            return;
        }

        await FinishResumeAsync(document, cancellationToken);
    }

    /// <summary>
    /// Enters <paramref name="candidate"/> and restores <paramref name="document"/>
    /// inside one plugin-session rebind when the directory changes.
    /// </summary>
    private async Task EnterWorkspaceAndResumeAsync(
        string candidate,
        SessionDocument document,
        CancellationToken cancellationToken)
    {
        if (string.Equals(candidate, _workspace.Root, StringComparison.Ordinal))
        {
            await FinishResumeAsync(document, cancellationToken);
            return;
        }

        if (!await ConfirmWorkspaceAsync(candidate, cancellationToken))
        {
            Note("Staying in " + _workspace.Root);
            return;
        }

        await RebindPluginSessionAsync(
            () =>
            {
                ApplyWorkspace(candidate);
                Note("Workspace  " + _workspace.Root);
                ApplyResume(document);
            },
            cancellationToken);
    }

    private async Task ResumeLoadedAsync(string? id, CancellationToken cancellationToken)
    {
        if (!SessionResume.TryLoad(
                _sessionStore,
                _workspace.Root,
                id,
                out var document,
                out var error))
        {
            Error(error);
            return;
        }

        await FinishResumeAsync(document, cancellationToken);
    }

    private async Task FinishResumeAsync(SessionDocument document, CancellationToken cancellationToken)
    {
        await RebindPluginSessionAsync(() => ApplyResume(document), cancellationToken);
    }

    private void ApplyResume(SessionDocument document)
    {
        CancelAndClearSide(announce: true);
        ApplyDocument(document);
        DiscardQueue();
        PresentResume();
    }

    private async Task ForkSessionAsync(string argument, CancellationToken cancellationToken)
    {
        SessionDocument source;
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (!HasConversation())
            {
                Error("Nothing to fork.");
                return;
            }

            source = CreateDocument();
            _sessionStore.Save(source);
        }
        else if (!SessionResume.TryLoad(
                     _sessionStore,
                     _workspace.Root,
                     argument,
                     out source,
                     out var error))
        {
            Error(error);
            return;
        }

        var sourceId = source.Id!;
        var fork = SessionFork.Create(
            source,
            SessionStore.NewId(),
            _workspace.Root,
            DateTimeOffset.UtcNow);
        await RebindPluginSessionAsync(
            () =>
            {
                CancelAndClearSide(announce: true);
                ApplyDocument(fork);
                SeedWorkspaceDefaults();
                SaveSession();
                RefreshChrome();
                ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
                Publish(new HistoryReplayed(ArchiveSnapshot()));
                ShowTodos();
                Note($"Forked  {sourceId}  ->  {_sessionId}");
            },
            cancellationToken);
    }

    private void ShowSessions(string argument)
    {
        var includeAll = string.Equals(argument, "all", StringComparison.OrdinalIgnoreCase);
        if (!includeAll && !string.IsNullOrWhiteSpace(argument))
        {
            Error("Usage: /sessions [all]");
            return;
        }

        var sessions = _sessionStore.List(includeAll ? null : _workspace.Root);
        if (sessions.Count == 0)
        {
            Note(includeAll ? "No sessions" : "No sessions for this workspace");
            return;
        }

        Note(SessionListText.Format(sessions, _sessionId, includeAll));
    }

    private void ShowStats(string argument)
    {
        if (!SessionStatsArguments.TryParse(argument, out var options, out var error))
        {
            Error(error);
            return;
        }

        var summaries = _sessionStore.List(options.IncludeAllWorkspaces ? null : _workspace.Root);
        var sessions = new List<SessionDocument>(summaries.Count);
        foreach (var summary in summaries)
        {
            if (_sessionStore.TryLoad(summary.Id, out var document))
            {
                sessions.Add(document);
            }
        }

        var report = SessionStatsCompiler.Compile(
            sessions,
            DateTimeOffset.UtcNow,
            options.WindowDays,
            options.TopTools);
        Publish(new StatsReported(report, options.IncludeAllWorkspaces));
    }

    private void ApplyDocument(SessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var access = (document.Access ?? []).ToArray();
        if (!DirectoryAccessPolicy.TryValidateSaved(access, out var accessError))
        {
            throw new ArgumentException(accessError, nameof(document));
        }

        _workspace.Access.Clear();
        foreach (var rule in access)
        {
            // Keep the authorized canonical root, not a newly retargeted symlink.
            _workspace.Access.Set(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Path)), rule.Mode,
                rule.SourcePath is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.SourcePath)));
        }

        Publish(new ImageHistoryInvalidated());
        var items = TranscriptCodec.Read(document.Items);
        _sessionId = document.Id!;
        _sessionCreatedUtc = document.CreatedUtc ?? DateTimeOffset.UtcNow;
        _planMode = document.PlanMode;
        var imageNumbers = document.Images.Where(static image => image is not null)
            .Select(static image => image.Number)
            .ToHashSet();
        _transcript = document.ImageMarkersTagged
            ? items
            : ImageMarkerText.TagLegacy(items, imageNumbers);
        var archive = document.Archive is null
            ? null
            : TranscriptCodec.Read(document.Archive);
        lock (_archiveGate)
        {
            _archive = archive is null
                ? [.. _transcript]
                : document.ImageMarkersTagged
                    ? [.. archive]
                    : ImageMarkerText.TagLegacy(archive, imageNumbers);
        }
        lock (_imagesGate)
        {
            _images = new Dictionary<int, ImageAttachment>(
                _sessionStore.ReadImages(document.Images));
        }
        var availableImages = ImageSnapshot();
        _unavailableImages = document.Images
            .Where(image => image is not null
                && image.Number > 0
                && !availableImages.ContainsKey(image.Number))
            .ToList();
        _pendingImages.Clear();
        _draftImages.Clear();
        lock (_imagesGate)
        {
            _nextImageNumber = NextImageNumber();
        }
        ReplaceLiveSystem();

        _todos.Clear();
        _todos.Replace(SessionMapper.ReadTodos(document.Todos));
        _ledger.Restore(
            Math.Max(0, document.UserTurns),
            Math.Max(0, document.ModelCalls),
            Math.Max(0, document.ToolCalls),
            SessionMapper.ReadUsage(document.Usage),
            SessionMapper.ReadUsage(document.CumulativeUsage));
        _queue.Clear();
        BindReviewConversation();
        RebuildExecutors();
    }

    private void PresentResume()
    {
        RefreshChrome();
        ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
        Publish(new HistoryReplayed(ArchiveSnapshot()));
        Note("Resumed  " + _sessionId);
        if (_unavailableImages.Count > 0)
        {
            Note(
                $"{_unavailableImages.Count} saved image attachment(s) unavailable; "
                + "session text remains available.");
        }

        ShowTodos();
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
        ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
    }

    private bool HasConversation() =>
        TranscriptCodec.HasConversation(_transcript)
        || TranscriptCodec.HasConversation(ArchiveSnapshot());

    private void RebuildExecutors()
    {
        var approvalPrompt = _frontEnd.Approvals;
        var question = _frontEnd.Questions;
        var reviewer = new ModelApprovalReviewer(
            ReviewerClient(),
            CurrentReviewSystemText(),
            ReviewerReasoning(),
            request => ApprovalReviewPrompt.UserText(
                request,
                CurrentPromptBinding(CurrentPromptContext().WithMode("review")),
                _prompts.ReviewUser),
            _settings.ApprovalRetry.ToOptions());
        var policy = new ApprovalPolicy(
            _approval,
            _workspace,
            _grants,
            approvalPrompt,
            reviewer,
            _reviewContext,
            [.. _plugins.Classifiers, .. _loadedPlugins.Classifiers, _external.Classifier],
            _skills,
            _external.AutomaticTools,
            (call, classification) => _hooks.OnApproval(call, classification));
        var options = new ToolExecutionOptions(ToolExecutionMode.Serial, 1);
        var imageDescriber = CreateImageDescriber();
        var workExecutor = new ToolExecutor(
            WorkspaceCatalog.CreateWork(
                _workspace,
                _todos,
                question,
                _plugins,
                _skills,
                _external,
                _settings.BashTimeoutSeconds,
                _loadedPlugins,
                imageDescriber,
                _backgroundTasks,
                _settings.DisabledBuiltInTools),
            options,
            policy.DecideAsync,
            HarnessExceptionMapper.MapAsync);
        var planExecutor = new ToolExecutor(
            WorkspaceCatalog.CreatePlan(
                _workspace,
                _todos,
                question,
                _plugins,
                _skills,
                _external,
                _loadedPlugins,
                imageDescriber,
                _settings.DisabledBuiltInTools),
            options,
            policy.DecideAsync,
            HarnessExceptionMapper.MapAsync);
        _workExecutor = new PluginToolExecutor(workExecutor, _hooks);
        _planExecutor = new PluginToolExecutor(planExecutor, _hooks);
        _workMultimodalExecutor = new PluginMultimodalExecutor(
            new HybridMultimodalToolExecutor(
                workExecutor,
                CreateMultimodalTools(question, HostToolCatalog.Work),
                policy),
            _hooks);
        _planMultimodalExecutor = new PluginMultimodalExecutor(
            new HybridMultimodalToolExecutor(
                planExecutor,
                CreateMultimodalTools(question, HostToolCatalog.Plan),
                policy),
            _hooks);
    }

    private IReadOnlyList<IMultimodalTool> CreateMultimodalTools(
        IUserPrompt prompt,
        HostToolCatalog catalog)
    {
        if (_multimodalClient is null)
        {
            return [];
        }

        var tools = new List<IMultimodalTool>(
            _plugins.CreateMultimodalTools(
                _workspace,
                _todos,
                prompt,
                catalog));
        switch (catalog)
        {
            case HostToolCatalog.Plan:
                tools.AddRange(_loadedPlugins.PlanMultimodalTools);
                tools.AddRange(_external.PlanMultimodalTools);
                break;
            case HostToolCatalog.Work:
                tools.AddRange(_loadedPlugins.WorkMultimodalTools);
                tools.AddRange(_external.WorkMultimodalTools);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(catalog));
        }

        // Text-only turns return before this, so view_image stays off that catalog.
        tools.AddRange(ViewImageTool.ForSession(_workspace, sessionAcceptsImages: true));
        if (_settings.DisabledBuiltInTools.Count > 0)
        {
            tools.RemoveAll(tool =>
                BuiltInToolSelection.Includes(
                    _settings.DisabledBuiltInTools,
                    tool.Definition.Name));
        }

        return tools;
    }

    private void ReloadSkills()
    {
        _skills = _settings.Skills
            ? _skillDiscovery.Collect(_workspace.Root)
            : null;
    }

    private void ReloadExternalTools()
    {
        _external = ExternalCatalog.Load(
            _home,
            _workspace,
            _settings.ExternalTools,
            _settings.ExternalToolApproval,
            _toolHost,
            _loadedPlugins.ToolNames);
    }

    private void ReloadPlugins()
    {
        _statusBoard.Retire();
        _loadedPlugins = PluginCatalog.Load(_home, _workspace, _settings.Plugins, _toolHost);
        _hooks = new PluginHookPipeline(_loadedPlugins.Hooks, Note, _loadedPlugins.RawHooks);
        _placeholders = new PluginPlaceholderTable(_loadedPlugins.Placeholders, Note);
        _placeholders.SetModels(_modelView);
        _statusBoard = new PluginStatusBoard(PublishStatusFields, Note);
        _statusAttached = false;
    }

    private void PublishStatusFields(IReadOnlyList<StatusFieldValue> fields)
    {
        Publish(new StatusFieldsChanged(fields));
    }

    private void ReloadPluginsWithProgress()
    {
        if (_settings.Plugins)
        {
            SetActivity(SessionActivity.LoadingPlugins);
        }

        try
        {
            ReloadPlugins();
        }
        finally
        {
            if (_settings.Plugins)
            {
                SetActivity(SessionActivity.Idle);
            }
        }
    }

    private void WritePluginNotes()
    {
        foreach (var note in _loadedPlugins.Notes)
        {
            Note(note);
        }
    }

    /// <summary>
    /// Ends the plugin session while the current snapshot is still current,
    /// applies <paramref name="change"/>, then starts it again when it was open.
    /// </summary>
    private async Task RebindPluginSessionAsync(Action change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var reopen = _pluginSessionOpen;
        if (reopen)
        {
            await ClosePluginSessionAsync(cancellationToken);
        }

        try
        {
            change();
        }
        catch (Exception)
        {
            if (reopen)
            {
                await OpenPluginSessionAsync(cancellationToken);
            }

            throw;
        }

        if (reopen)
        {
            await OpenPluginSessionAsync(cancellationToken);
        }
    }

    private async Task OpenPluginSessionAsync(CancellationToken cancellationToken)
    {
        if (_pluginSessionOpen || _loadedPlugins.Hooks.Count == 0)
        {
            return;
        }

        _pluginSessionOpen = true;
        await _hooks.StartAsync(CurrentPluginSession(), cancellationToken);
    }

    private async Task ClosePluginSessionAsync(CancellationToken cancellationToken)
    {
        if (!_pluginSessionOpen)
        {
            return;
        }

        _pluginSessionOpen = false;
        await _hooks.EndAsync(CurrentPluginSession(), cancellationToken);
    }

    private PluginSession CurrentPluginSession() =>
        new(_workspace.Root, _sessionId, _approval.Value);

    private async Task ChangePluginsAsync(string argument, CancellationToken cancellationToken)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            foreach (var line in _loadedPlugins.Describe(_settings.Plugins))
            {
                Note(line);
            }

            var hidden = PluginInventory.List(_home, _workspace.Root)
                .Where(entry => !_loadedPlugins.Plugins.Any(plugin =>
                    plugin.Source == entry.Source
                    && ExternalToolNames.OverlayComparer.Equals(
                        plugin.DirectoryName,
                        entry.DirectoryName)))
                .ToArray();
            if (hidden.Length > 0)
            {
                foreach (var line in PluginInventory.Format(hidden))
                {
                    Note(line);
                }
            }

            if (!_settings.Plugins)
            {
                Note(PluginInventory.DiscoveryOff);
            }

            return;
        }

        if (CatalogCommand.LooksLike(parts))
        {
            await ChangePluginDirectoryAsync(parts, cancellationToken);
            return;
        }

        var command = parts[0].ToLowerInvariant();
        if (parts.Length != 1 || command is not ("on" or "off" or "reload"))
        {
            Error(
                "Plugins command must be /plugins, /plugins on|off|reload, "
                + "/plugins enable|disable|show <directory>, "
                + "or /plugins home|project enable|disable|show <directory>.");
            return;
        }

        if (_turnActive)
        {
            Error("Finish the current turn before reloading plugins.");
            return;
        }

        await ClosePluginSessionAsync(cancellationToken);
        if (command != "reload")
        {
            SaveSettings(current => current.WithPlugins(command == "on"));
        }

        ReloadPluginsWithProgress();
        ReloadExternalToolsWithProgress();
        PublishPluginEnvironment();
        try
        {
            ReplaceClients();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Error(exception.Message);
        }

        RebuildExecutors();
        ReplaceLiveSystem();
        WritePluginNotes();
        WriteExternalNotes();
        RefreshSlashCommands();
        await OpenPluginSessionAsync(cancellationToken);
        Note(command == "reload" ? "Plugins reloaded" : "Plugins  " + Title(command));
    }

    private async Task ChangePluginDirectoryAsync(
        IReadOnlyList<string> parts,
        CancellationToken cancellationToken)
    {
        if (!CatalogCommand.TryParse(parts, out var command, out var error) || command is null)
        {
            Error(error);
            return;
        }

        if (_turnActive && command.Verb != "show")
        {
            Error("Finish the current turn before reloading plugins.");
            return;
        }

        var source = command.Source switch
        {
            "home" => PluginSource.Home,
            "project" => PluginSource.Project,
            _ => (PluginSource?)null
        };
        if (command.Verb == "show")
        {
            if (!PluginInventory.TryFind(_home, _workspace.Root, command.DirectoryName, source, out var entry, out error)
                || entry is null)
            {
                Error(error);
                return;
            }

            foreach (var line in PluginInventory.Format(entry))
            {
                Note(line);
            }

            if (!_settings.Plugins)
            {
                Note(PluginInventory.DiscoveryOff);
            }

            return;
        }

        var enabled = command.Verb == "enable";
        if (!PluginInventory.TrySetEnabled(
                _home,
                _workspace.Root,
                command.DirectoryName,
                source,
                enabled,
                out var updated,
                out var changed,
                out error)
            || updated is null)
        {
            Error(error);
            return;
        }

        if (changed)
        {
            await ClosePluginSessionAsync(cancellationToken);
            ReloadPluginsWithProgress();
            ReloadExternalToolsWithProgress();
            PublishPluginEnvironment();
            try
            {
                ReplaceClients();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Error(exception.Message);
            }

            RebuildExecutors();
            ReplaceLiveSystem();
            WritePluginNotes();
            WriteExternalNotes();
            RefreshSlashCommands();
            await OpenPluginSessionAsync(cancellationToken);
        }

        Note(changed
            ? $"Plugin {updated.DirectoryName}  {(enabled ? "Enabled" : "Disabled")}"
            : $"Plugin {updated.DirectoryName} is already {(enabled ? "enabled" : "disabled")}.");
        if (enabled && !_settings.Plugins)
        {
            Note(PluginInventory.DiscoveryOff);
        }
    }

    private IReadOnlyList<ISlashCommand> PluginCommands()
    {
        var commands = new List<ISlashCommand>(_plugins.Commands);
        commands.AddRange(_loadedPlugins.Commands);
        return commands;
    }

    private void ReloadExternalToolsWithProgress()
    {
        if (_settings.ExternalTools)
        {
            SetActivity(SessionActivity.LoadingTools);
        }

        try
        {
            ReloadExternalTools();
        }
        finally
        {
            if (_settings.ExternalTools)
            {
                SetActivity(SessionActivity.Idle);
            }
        }
    }

    private void PublishPluginEnvironment()
    {
        var environment = PluginEnvironmentFactory.Create(
            _home,
            _workspace.Root,
            _loadedPlugins,
            _external,
            _skills);
        _placeholders.SetEnvironment(environment);
        _placeholders.SetModels(_modelView);
        _loadedPlugins.Attach(environment, Note);
        _loadedPlugins.AttachSession(_modelView, Note);
        _loadedPlugins.AttachClients(_clientView, Note);
        _loadedPlugins.AttachDataDirectories(_home, _workspace.Root, Note);
        if (!_statusAttached)
        {
            _statusAttached = true;
            _loadedPlugins.AttachStatus(_statusBoard, Note);
        }
    }

    private void WriteExternalNotes()
    {
        foreach (var note in _external.Notes)
        {
            Note(note);
        }
    }

    private void BindReviewConversation()
    {
        _reviewContext.Conversation = _transcript;
    }

    private IChatClient ReviewerClient()
    {
        if (!_settings.ApprovalModel.Enabled)
        {
            ReleaseApprovalClient();
            _clientView.ReleaseReview();
            return _client;
        }

        EnsureApprovalClient();
        return _approvalClient!;
    }

    private ReasoningOptions? ReviewerReasoning()
    {
        if (!_settings.ApprovalModel.Enabled)
        {
            return CurrentReasoning();
        }

        var model = _settings.Catalog.GetModel(
            new ProviderName(_settings.ApprovalModel.Provider!),
            _settings.ApprovalModel.Model!);
        return _settings.ApprovalModel.ThinkingEffort.ToReasoningOptions(model);
    }

    private void EnsureApprovalClient()
    {
        var key = ApprovalClientKey(_settings.ApprovalModel);
        if (_approvalClient is not null && _approvalClientKey == key)
        {
            return;
        }

        if (!TryPrepareApprovalClient(_settings.ApprovalModel, out var client, out var error)
            || client is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "The approval model could not be created."
                    : error);
        }

        ReleaseApprovalClient();
        _approvalClient = client;
        _approvalClientKey = key;
        _clientView.ReleaseReview();
    }

    private void ReleaseApprovalClient()
    {
        if (_approvalClient is null)
        {
            return;
        }

        if (!ReferenceEquals(_approvalClient, _client))
        {
            DisposeClient(_approvalClient);
        }

        _approvalClient = null;
        _approvalClientKey = null;
    }

    private string ApprovalClientKey(ApprovalModelSettings settings)
    {
        var provider = new ProviderName(settings.Provider!);
        return settings.Provider
            + "\n" + settings.Model
            + "\n" + _settings.TierFor(provider, settings.Model!)
            + "\n" + _settings.SpeedFor(provider, settings.Model!);
    }

    private void ChangeCompactionModel(CompactionModelArguments.Request request)
    {
        if (request.Show)
        {
            Note(_settings.CompactionModel.Describe());
            return;
        }

        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before changing the compaction model.");
            return;
        }

        CompactionModelSettings next;
        if (request.Enabled is bool enabled)
        {
            if (enabled && !_settings.CompactionModel.HasSelection)
            {
                Error("Set a compaction model before turning it on.");
                return;
            }

            next = enabled
                ? _settings.CompactionModel.EnabledCopy()
                : _settings.CompactionModel.DisabledCopy();
        }
        else if (!TrySelectCompactionModel(request.Selection ?? string.Empty, out next, out var selectError))
        {
            Error(selectError);
            return;
        }

        if (!TryPrepareCompactionClient(next, out var client, out var prepareError))
        {
            Error(prepareError);
            return;
        }

        // A launch gear is process-only. This command changes the switch or
        // the selection, so the saved gear stays whatever config.json held.
        var persistedNext = next.WithThinkingEffort(
            _persisted.CompactionModel.ThinkingEffort);
        HarnessSettings updated;
        try
        {
            updated = _settings.WithCompactionModel(next);
            _ = _persisted.WithCompactionModel(persistedNext);
        }
        catch (InvalidOperationException exception)
        {
            if (client is not null && !ReferenceEquals(client, _client))
            {
                DisposeClient(client);
            }

            Error(exception.Message);
            return;
        }

        _settings = updated;
        _persisted = _persisted.WithCompactionModel(persistedNext);
        _settingsStore.Save(_persisted);
        ReplaceCompactionClient(next, client);
        RefreshSlashCommands();
        Note(next.Describe());
    }

    private void ChangeCompactionSummary(CompactionSummaryArguments.Request request)
    {
        var enabled = request.Enabled ?? !(request.Automatic
            ? _settings.ShowAutomaticCompactionSummary
            : _settings.ShowCompactionSummary);
        SaveSettings(current => request.Automatic
            ? current.WithShowAutomaticCompactionSummary(enabled)
            : current.WithShowCompactionSummary(enabled));
        Note(request.Automatic
            ? "Automatic compaction summary  " + (enabled ? "On" : "Off")
            : "Compaction summary  " + (enabled ? "On" : "Off"));
    }

    private void ChangeMicroCompaction(bool? enabled)
    {
        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before changing microcompaction.");
            return;
        }

        var next = enabled ?? !_settings.MicroCompaction.Enabled;
        SaveSettings(current => current.WithMicroCompaction(current.MicroCompaction.WithEnabled(next)));
        Note("Microcompaction  " + (next ? "On" : "Off"));
    }

    /// <summary>
    /// Replaces old successful tool results when microcompaction is on.
    /// A rewritten session transcript is saved. A copy, such as a side
    /// question, is pruned without writing it back. After a rewrite the
    /// caller must judge the new estimate: the previous provider usage
    /// still counts the removed text.
    /// </summary>
    private bool TryMicrocompact(IReadOnlyList<ChatItem> transcript, out IReadOnlyList<ChatItem> pruned)
    {
        pruned = transcript;
        var settings = _settings.MicroCompaction;
        if (!settings.Enabled)
        {
            return false;
        }

        var next = ToolResultPruner.Prune(
            transcript,
            ContextCompactor.OmittedResultText,
            settings.ProtectTokens,
            settings.MinimumTokens,
            settings.ProtectTurns);
        if (ReferenceEquals(next, transcript))
        {
            return false;
        }

        if (ReferenceEquals(transcript, _transcript))
        {
            _transcript.Clear();
            _transcript.AddRange(next);
            BindReviewConversation();
            RememberCompactedUsage();
            SaveSession();
            pruned = _transcript;
            return true;
        }

        pruned = next;
        return true;
    }

    private void ChangeCompactionThinking(CompactionThinkingArguments.Request request)
    {
        if (_turnActive || _compactSource is not null)
        {
            Error("Finish the current turn before changing the compaction model.");
            return;
        }

        var compaction = _settings.CompactionModel;
        if (!compaction.HasSelection)
        {
            Error("Set a compaction model before changing its thinking gear.");
            return;
        }

        ModelSettings model;
        try
        {
            model = _settings.Catalog.GetModel(
                new ProviderName(compaction.Provider!),
                compaction.Model!);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            Error(exception.Message);
            return;
        }

        if (!model.Thinking)
        {
            Error("The compaction model does not support thinking.");
            return;
        }

        var selection = compaction.ThinkingEffort;
        if (request.Effort is null)
        {
            selection = ThinkingSelection.Next(selection, model);
        }
        else
        {
            ThinkingSelection parsed;
            try
            {
                parsed = ThinkingSelection.Parse(request.Effort);
            }
            catch (ArgumentException exception)
            {
                Error(exception.Message);
                return;
            }

            if (parsed == ThinkingSelection.Off && !model.ThinkingCanDisable)
            {
                Error("The compaction model cannot disable thinking.");
                return;
            }

            if (parsed != ThinkingSelection.Default
                && parsed != ThinkingSelection.Off
                && !model.AllowsEffort(parsed.Value))
            {
                Error(
                    $"Thinking effort '{parsed.Value}' is not available for the compaction model.");
                return;
            }

            selection = parsed;
        }

        SaveSettings(current => current.WithCompactionModel(
            current.CompactionModel.WithThinkingEffort(selection)));
        _compactor = CreateCompactor(CompactionClient(), CompactionReasoning());
        RefreshSlashCommands();
        Note("Compaction thinking  " + ThinkingLabel.For(selection));
    }

    private bool TrySelectCompactionModel(
        string selectionText,
        out CompactionModelSettings settings,
        out string error)
    {
        settings = CompactionModelSettings.Off;
        var currentProvider = _settings.CompactionModel.Provider is string stored
            ? new ProviderName(stored)
            : _settings.Provider;
        if (!ModelSelection.TryResolve(
                _settings.Catalog,
                currentProvider,
                selectionText,
                out var selection,
                out error)
            || selection is null)
        {
            error = (error ?? string.Empty).Replace(
                "/model",
                "/compact model",
                StringComparison.Ordinal);
            return false;
        }

        settings = new CompactionModelSettings(
            true,
            selection.Provider.Value,
            selection.Model,
            _settings.CompactionModel.ThinkingEffort);
        error = string.Empty;
        return true;
    }

    private bool TryPrepareCompactionClient(
        CompactionModelSettings settings,
        out IStreamingChatClient? client,
        out string error)
    {
        client = null;
        error = string.Empty;
        if (!settings.Enabled)
        {
            return true;
        }

        try
        {
            client = CreateCompactionClient(settings);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException
            or NotSupportedException)
        {
            error = exception.Message;
            return false;
        }
    }

    private IStreamingChatClient CreateCompactionClient(CompactionModelSettings settings)
    {
        var selected = _settings.WithSelection(
            new ProviderName(settings.Provider!),
            settings.Model!);
        return CreateClient(selected);
    }

    private void ReplaceCompactionClient(CompactionModelSettings settings, IStreamingChatClient? client)
    {
        ReleaseCompactionClient();
        if (client is null)
        {
            _compactor = CreateCompactor(_client);
            return;
        }

        _compactionClient = client;
        _compactionClientKey = CompactionClientKey(settings);
        _compactor = CreateCompactor(client, CompactionReasoning());
    }

    private void BindCompactor()
    {
        ReleaseCompactionClient();
        if (!_settings.CompactionModel.Enabled)
        {
            _compactor = CreateCompactor(_client);
            return;
        }

        if (!TryPrepareCompactionClient(_settings.CompactionModel, out var client, out var error)
            || client is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "The compaction model could not be created."
                    : error);
        }

        _compactionClient = client;
        _compactionClientKey = CompactionClientKey(_settings.CompactionModel);
        _compactor = CreateCompactor(client, CompactionReasoning());
    }

    private void EnsureCompactionClient()
    {
        if (!_settings.CompactionModel.Enabled)
        {
            return;
        }

        var key = CompactionClientKey(_settings.CompactionModel);
        if (_compactionClient is not null && _compactionClientKey == key)
        {
            return;
        }

        if (!TryPrepareCompactionClient(_settings.CompactionModel, out var client, out var error)
            || client is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "The compaction model could not be created."
                    : error);
        }

        ReplaceCompactionClient(_settings.CompactionModel, client);
    }

    private void ReleaseCompactionClient()
    {
        if (_compactionClient is null)
        {
            return;
        }

        if (!ReferenceEquals(_compactionClient, _client))
        {
            DisposeClient(_compactionClient);
        }

        _compactionClient = null;
        _compactionClientKey = null;
    }

    private IChatClient CompactionClient() =>
        _settings.CompactionModel.Enabled && _compactionClient is not null
            ? _compactionClient
            : _client;

    private IStreamingChatClient OpenSideCompactionClient(
        IStreamingChatClient answerClient,
        out bool owned)
    {
        if (!_settings.CompactionModel.Enabled)
        {
            owned = false;
            return answerClient;
        }

        var client = CreateCompactionClient(_settings.CompactionModel);
        owned = !ReferenceEquals(client, _client)
            && !ReferenceEquals(client, _compactionClient)
            && !ReferenceEquals(client, answerClient);
        return client;
    }

    private ReasoningOptions? CompactionReasoning()
    {
        if (!_settings.CompactionModel.Enabled
            || _settings.CompactionModel.Provider is null
            || _settings.CompactionModel.Model is null)
        {
            return null;
        }

        var model = _settings.Catalog.GetModel(
            new ProviderName(_settings.CompactionModel.Provider),
            _settings.CompactionModel.Model);
        return _settings.CompactionModel.ThinkingEffort.ToReasoningOptions(model);
    }

    private int? CompactionSummaryWindow()
    {
        var compaction = _settings.CompactionModel;
        if (!compaction.Enabled || compaction.Provider is null || compaction.Model is null)
        {
            return null;
        }

        try
        {
            var declared = _settings.Catalog.GetModel(
                new ProviderName(compaction.Provider),
                compaction.Model).ContextWindow;
            return Math.Min(declared, _settings.ContextWindow);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    private string? CompactionModelStatus()
    {
        var compaction = _settings.CompactionModel;
        if (!compaction.Enabled || !compaction.HasSelection)
        {
            return null;
        }

        var text = compaction.Provider + " / " + compaction.Model;
        try
        {
            var model = _settings.Catalog.GetModel(
                new ProviderName(compaction.Provider!),
                compaction.Model!);
            var thinking = ThinkingStatus.For(model, compaction.ThinkingEffort);
            if (thinking.Length > 0 && thinking != "Think Default")
            {
                text += "  ·  " + thinking;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return text;
        }

        return text;
    }

    private string CompactionClientKey(CompactionModelSettings settings)
    {
        var provider = new ProviderName(settings.Provider!);
        return settings.Provider
            + "\n" + settings.Model
            + "\n" + _settings.TierFor(provider, settings.Model!)
            + "\n" + _settings.SpeedFor(provider, settings.Model!);
    }

    private ReasoningOptions? CurrentReasoning() =>
        _thinkingEffort.ToReasoningOptions(_settings.ActiveModel);

    private string CurrentThinkingStatus() =>
        ThinkingStatus.For(_settings.ActiveModel, _thinkingEffort);

    private string CurrentApprovalThinkingStatus()
    {
        var model = _settings.Catalog.GetModel(
            new ProviderName(_settings.ApprovalModel.Provider!),
            _settings.ApprovalModel.Model!);
        return ThinkingStatus.For(model, _settings.ApprovalModel.ThinkingEffort);
    }

    private string CurrentPromptStatus() =>
        string.Equals(
            _promptResolution.PromptSet,
            PromptSetNames.Default,
            StringComparison.Ordinal)
                ? string.Empty
                : _promptResolution.PromptSet;

    private void WritePromptNotes()
    {
        foreach (var note in _promptResolution.Notes)
        {
            Note(note);
        }
    }

    private void RefreshSlashCommands()
    {
        var menu = SlashMenu.Create(
            PluginCommands(),
            ThinkingCompletions.For(_settings.ActiveModel),
            ModelCompletions.For(_settings.Catalog, _settings.Provider),
            PromptSetCompletions.For(_promptResolution),
            PromptAttachmentCompletions.For(_promptResolution),
            ToolCompletions.All,
            ExportCompletions.All,
            ApprovalModelCompletions(),
            ApprovalThinkingCompletions(),
            ContextWindowSelection.Completions(_settings.ActiveModel),
            CompactionModelCompletions(),
            CompactionThinkingCompletions(),
            CatalogSlotCompletions.For(_settings.ActiveModel.ServiceTiers),
            CatalogSlotCompletions.For(_settings.ActiveModel.Speeds));
        Publish(new SlashCommandsChanged(menu));
    }

    private IReadOnlyList<SlashCompletion> ApprovalModelCompletions()
    {
        var options = new List<SlashCompletion>
        {
            new("on", "Turn the approval model on", ["on"]),
            new("off", "Turn the approval model off", ["off"])
        };
        var provider = _settings.Provider;
        if (_settings.ApprovalModel.Provider is string stored)
        {
            try
            {
                var parsed = ProviderName.Parse(stored);
                _ = _settings.Catalog.Get(parsed);
                provider = parsed;
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                provider = _settings.Provider;
            }
        }

        options.AddRange(ModelCompletions.For(_settings.Catalog, provider));
        return options;
    }

    private IReadOnlyList<SlashCompletion> ApprovalThinkingCompletions()
    {
        var approval = _settings.ApprovalModel;
        if (approval.Provider is null || approval.Model is null)
        {
            return [];
        }

        try
        {
            return ThinkingCompletions.For(
                _settings.Catalog.GetModel(
                    new ProviderName(approval.Provider),
                    approval.Model));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return [];
        }
    }

    private IReadOnlyList<SlashCompletion> CompactionModelCompletions()
    {
        var options = new List<SlashCompletion>
        {
            new("on", "Turn the compaction model on", ["on"]),
            new("off", "Turn the compaction model off", ["off"])
        };
        var provider = _settings.Provider;
        if (_settings.CompactionModel.Provider is string stored)
        {
            try
            {
                var parsed = ProviderName.Parse(stored);
                _ = _settings.Catalog.Get(parsed);
                provider = parsed;
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                provider = _settings.Provider;
            }
        }

        options.AddRange(ModelCompletions.For(_settings.Catalog, provider));
        return options;
    }

    private IReadOnlyList<SlashCompletion> CompactionThinkingCompletions()
    {
        var compaction = _settings.CompactionModel;
        if (compaction.Provider is null || compaction.Model is null)
        {
            return [];
        }

        try
        {
            return ThinkingCompletions.For(
                _settings.Catalog.GetModel(
                    new ProviderName(compaction.Provider),
                    compaction.Model));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return [];
        }
    }

    private SessionChrome CurrentChrome() =>
        new(
            _settings.Model,
            _workspace.Root,
            _planMode,
            _approval,
            CurrentThinkingStatus(),
            CurrentPromptStatus(),
            _settings.ContextWindow,
            CatalogSlotStatus.For("Tier", _settings.ServiceTier),
            CatalogSlotStatus.For("Speed", _settings.Speed));

    private void RefreshChrome()
    {
        Publish(new ChromeChanged(CurrentChrome()));
    }

    private SessionPreferences CurrentPreferences() =>
        new(
            _settings.EstimatedTokens,
            _settings.VerboseTools,
            _settings.VerboseCommands,
            _settings.VerboseResults,
            _settings.VerboseCalls,
            _settings.VerboseApprovals,
            _settings.VerboseReview,
            _settings.VerboseThinking,
            _settings.StatusLine.Enabled,
            _settings.StatusLine.Fields);

    private void PublishPreferences()
    {
        Publish(new PreferencesChanged(CurrentPreferences()));
    }

    private void PromoteAfterTools()
    {
        ShowTodos();
        if (_queue.InterruptAfterTools)
        {
            _turnSource?.Cancel();
        }
    }

    private void ShowTodos()
    {
        Publish(new TodosChanged(_todos.Snapshot()));
    }

    private void ShowTodoList()
    {
        Note(_todos.Format());
    }

    private void ShowQueue()
    {
        Publish(new QueueChanged(QueueSnapshot()));
    }

    private IReadOnlyList<string> QueueSnapshot() =>
        _deferredStops.Count == 0
            ? _queue.Snapshot()
            : [.. _deferredStops, .. _queue.Snapshot()];

    // Follow-up text only. Leave _deferredStops so a later /clear, /resume,
    // or /fork still runs in the order it was typed.
    private void DiscardQueue()
    {
        _queue.Clear();
        ShowQueue();
    }

    /// <summary>
    /// The side question currently in flight, if one has been started.
    /// </summary>
    internal Task SideQuestionTask => _sideTask ?? Task.CompletedTask;

    /// <summary>
    /// Cancels the side question without interrupting the main turn.
    /// </summary>
    public bool TryCancelSideQuestion()
    {
        lock (_sideGate)
        {
            if (!_sideRunning || _sideSource is null)
            {
                return false;
            }

            _sideSource.Cancel();
            return true;
        }
    }

    /// <summary>
    /// Drops the in-memory side questions for this process.
    /// </summary>
    public void ClearSideQuestions()
    {
        CancelAndClearSide(announce: true);
    }

    /// <summary>
    /// Adds a follow-up that is sent when the current tool batch or turn ends.
    /// The pump cannot re-enter <see cref="SubmitAsync"/>, so a slash command
    /// is handled here. Commands that only report state run immediately.
    /// <c>/clear</c>, <c>/resume</c>, and <c>/fork</c> cancel the busy turn,
    /// stay visible in the queue, and run in order when that work unwinds.
    /// During compaction they are refused. <c>/quit</c> still cancels that
    /// work and leaves.
    /// Blank text does not enqueue; when a follow-up is already waiting it
    /// interrupts so that text is delivered now.
    /// </summary>
    public void Enqueue(string input) => AcceptQueuedInput(input, remember: true);

    private void AcceptQueuedInput(string input, bool remember)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input))
        {
            ReleaseQueuedFollowUp();
            return;
        }

        var trimmed = input.Trim();
        if (remember)
        {
            RememberPrompt(trimmed);
        }

        if (SessionCommand.TryParse(trimmed, out var command))
        {
            if (command.Verb == SessionVerb.Quit)
            {
                ClearDeferredStops();
                _quitRequested = true;
                TryInterrupt();
                return;
            }

            if (RunsWhileBusy(command.Verb))
            {
                if (!TryRunWhileBusy(command))
                {
                    RefuseCommand(trimmed);
                }

                return;
            }

            if (!_compacting && StopsBusyTurn(command.Verb))
            {
                _deferredStops.Add(trimmed);
                ShowQueue();
                TryInterrupt();
                return;
            }

            RefuseCommand(trimmed);
            return;
        }

        _queue.Enqueue(AttachPendingImages(trimmed));
        ShowQueue();
    }

    private void ReleaseQueuedFollowUp()
    {
        if (_queue.Count > 0 || _deferredStops.Count > 0)
        {
            TryInterrupt();
        }
    }

    private bool ConsumeQuit()
    {
        if (!_quitRequested)
        {
            return false;
        }

        _quitRequested = false;
        return true;
    }

    private bool RefuseCompactionCommand(string input)
    {
        if (!_compacting || !SessionCommand.TryParse(input, out var command))
        {
            return false;
        }

        if (command.Verb == SessionVerb.Quit || RunsWhileBusy(command.Verb))
        {
            return false;
        }

        RefuseCommand(input);
        return true;
    }

    private bool QuitDuringCompaction(string input) =>
        _compacting
        && SessionCommand.TryParse(input, out var command)
        && command.Verb == SessionVerb.Quit;

    private void RefuseCommand(string input)
    {
        Error("Finish the current turn before using " + CommandToken(input) + ".");
    }

    private static string CommandToken(string input)
    {
        var trimmed = input.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    internal static bool RunsWhileBusy(SessionVerb verb) =>
        verb is SessionVerb.Help
            or SessionVerb.Status
            or SessionVerb.Stats
            or SessionVerb.Todos
            or SessionVerb.Sessions
            or SessionVerb.Tokens
            or SessionVerb.Verbose
            or SessionVerb.StatusLine
            or SessionVerb.Btw
            or SessionVerb.Attach;

    internal bool TryRunWhileBusy(SessionCommand command)
    {
        switch (command.Verb)
        {
            case SessionVerb.Help:
                Publish(new HelpRequested(PluginCommands()));
                return true;
            case SessionVerb.Status:
                ShowStatus(command.Argument);
                return true;
            case SessionVerb.Stats:
                ShowStats(command.Argument);
                return true;
            case SessionVerb.Todos:
                ShowTodoList();
                return true;
            case SessionVerb.Sessions:
                ShowSessions(command.Argument);
                return true;
            case SessionVerb.Tokens:
                ChangeEstimatedTokens(command.Argument);
                return true;
            case SessionVerb.Verbose:
                ChangeVerbose(command.Argument);
                return true;
            case SessionVerb.StatusLine:
                ChangeStatusLine(command.Argument);
                return true;
            case SessionVerb.Btw:
                if (string.IsNullOrWhiteSpace(command.Argument))
                {
                    ShowSideQuestion();
                }
                else
                {
                    AskSide(command.Argument.Trim());
                }

                return true;
            case SessionVerb.Attach:
                AttachImage(command.Argument);
                return true;
            default:
                return false;
        }
    }

    private void ClearDeferredStops()
    {
        if (_deferredStops.Count == 0)
        {
            return;
        }

        _deferredStops.Clear();
        ShowQueue();
    }

    private async Task<bool> DrainDeferredStopAsync(CancellationToken cancellationToken)
    {
        var exit = false;
        while (_deferredStops.Count > 0)
        {
            var pending = _deferredStops[0];
            _deferredStops.RemoveAt(0);
            ShowQueue();
            if (exit)
            {
                continue;
            }

            if (_turnActive)
            {
                _turnSource?.Cancel();
                await FinishTurnAsync(cancellationToken);
            }

            if (_compacting)
            {
                RefuseCommand(pending);
                continue;
            }

            var handled = await TryHandleCommandAsync(pending, cancellationToken);
            exit |= handled.Exit;
        }

        return exit;
    }

    private async Task<bool> StartTurnIfQueuedAsync(CancellationToken cancellationToken)
    {
        if (_turnActive)
        {
            return false;
        }

        var next = _queue.Drain();
        ShowQueue();
        if (next is not null)
        {
            await StartTurnAsync(next, cancellationToken, attachPendingImages: false);
        }

        return false;
    }

    private async Task StartTurnAsync(
        string input,
        CancellationToken cancellationToken,
        bool attachPendingImages = true)
    {
        var message = attachPendingImages ? AttachPendingImages(input) : input;
        message = await _hooks.OnUserMessageAsync(message, cancellationToken);
        Publish(new UserMessageSent(message));
        var user = new ChatMessage(ChatRole.User, message);
        _transcript.Add(user);
        CommitArchive([user]);
        var images = ImageSnapshot();
        _draftImages.RemoveWhere(number => message.Contains(
            images[number].TrustedMarker,
            StringComparison.Ordinal));
        var turnSource = new CancellationTokenSource();
        _turnSource = turnSource;
        _turnActive = true;
        lock (_usageGate)
        {
            _turnCumulativeBaseline = _shownCumulative;
        }

        _turnUserText = message;
        _turnFault = null;
        try
        {
            await _hooks.OnTurnStartedAsync(
                new PluginTurn(_sessionId, CurrentModeName(), message),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _turnActive = false;
            turnSource.Dispose();
            _turnSource = null;
            _turnUserText = null;
            throw;
        }

        Publish(new TurnStarted());
        _turnTask = ExecuteTurnAsync(turnSource.Token);
    }

    private string CurrentModeName() => _planMode ? "plan" : "work";

    private PluginModelPurpose CurrentModelPurpose() =>
        _planMode ? PluginModelPurpose.Plan : PluginModelPurpose.Work;

    private Task<IReadOnlyList<ChatItem>> PrepareOutboundAsync(
        IReadOnlyList<ChatItem> items,
        CancellationToken cancellationToken) =>
        _hooks.PrepareModelAsync(
            CurrentModelPurpose(),
            items,
            ImageMediaTypes(),
            _multimodalClient is not null,
            cancellationToken);

    private Task ReportModelResponseAsync(
        PluginModelPurpose purpose,
        ChatResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Candidates.Count == 0)
        {
            return Task.CompletedTask;
        }

        var candidate = response.Candidates[0];
        return _hooks.OnModelResponseAsync(
            purpose,
            candidate.FinishReason,
            candidate.Items,
            response.Usage,
            ImageMediaTypes(),
            cancellationToken).AsTask();
    }

    private Dictionary<int, string> ImageMediaTypes()
    {
        var images = ImageSnapshot();
        var mediaTypes = new Dictionary<int, string>(images.Count);
        foreach (var pair in images)
        {
            mediaTypes.Add(pair.Key, pair.Value.MimeType);
        }

        return mediaTypes;
    }

    private Task<TurnResult> ExecuteTurnAsync(CancellationToken cancellationToken)
    {
        if (_multimodalClient is not null)
        {
            var multimodalTurn = new MultimodalStreamingTurn(
                _multimodalClient,
                _planMode ? _planMultimodalExecutor : _workMultimodalExecutor,
                _settings.ExecutionBudget,
                ImageSnapshot(),
                this,
                CurrentReasoning(),
                CompactRoundAsync,
                _settings.Retry.ToOptions(),
                ReserveImageNumber,
                AddImage,
                ImageSnapshot,
                CommitArchive,
                PrepareOutboundAsync,
                (response, token) => ReportModelResponseAsync(CurrentModelPurpose(), response, token));
            return multimodalTurn.RunAsync(_transcript, cancellationToken);
        }

        var turn = new StreamingTurn(
            _client,
            _planMode ? _planExecutor : _workExecutor,
            _settings.ExecutionBudget,
            this,
            CurrentReasoning(),
            CompactRoundAsync,
            _settings.Retry.ToOptions(),
            CommitArchive,
            PrepareOutboundAsync,
            (response, token) => ReportModelResponseAsync(CurrentModelPurpose(), response, token));
        return turn.RunAsync(_transcript, cancellationToken);
    }

    private string AttachPendingImages(string input)
    {
        if (_pendingImages.Count == 0)
        {
            return input;
        }

        var images = ImageSnapshot();
        var markers = string.Join(
            ' ',
            _pendingImages.Select(number => images[number].TrustedMarker));
        _pendingImages.Clear();
        return input.Length == 0 ? markers : input + "\n\n" + markers;
    }

    private bool HasReferencedImages()
    {
        var images = ImageSnapshot();
        if (images.Count == 0)
        {
            return false;
        }

        return _transcript.Any(item =>
        {
            var text = item switch
            {
                ChatMessage message => message.Text,
                ToolResult result => result.Text,
                _ => null
            };
            return text is not null && images.Values.Any(image =>
                text.Contains(image.TrustedMarker, StringComparison.Ordinal));
        });
    }

    private void CommitArchive(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_archiveGate)
        {
            foreach (var item in items)
            {
                if (item is ChatMessage message && message.Role == ChatRole.System)
                {
                    continue;
                }

                _archive.Add(item);
            }
        }
    }

    private IReadOnlyList<ChatItem> SideBaseSnapshot() =>
        _turnActive ? [.. _transcript] : ArchiveSnapshot();

    private List<ChatItem> ArchiveSnapshot()
    {
        lock (_archiveGate)
        {
            return [.. _archive];
        }
    }

    private (List<ChatItem> Snapshot, int Count) ArchiveSnapshotWithCount()
    {
        lock (_archiveGate)
        {
            return ([.. _archive], _archive.Count);
        }
    }

    private int ArchiveCount()
    {
        lock (_archiveGate)
        {
            return _archive.Count;
        }
    }

    private IReadOnlyList<ChatItem> SideConversation(IReadOnlyList<ChatItem> archive)
    {
        var body = archive;
        if (body.Count > 0
            && body[0] is ChatMessage system
            && system.Role == ChatRole.System
            && !CompactionSelection.IsSummary(system))
        {
            body = body.Skip(1).ToArray();
        }

        return [new ChatMessage(ChatRole.System, CurrentSystemText()), .. body];
    }

    private bool IsReferencedInTranscript(string marker, IReadOnlyList<ChatItem> archiveSnapshot) =>
        ContainsMarker(_transcript, marker) || ContainsMarker(archiveSnapshot, marker);

    private static bool ContainsMarker(IReadOnlyList<ChatItem> items, string marker) =>
        items.Any(item => item switch
        {
            ChatMessage message => message.Text.Contains(marker, StringComparison.Ordinal),
            ToolResult result => result.Text.Contains(marker, StringComparison.Ordinal),
            _ => false
        });

    private HashSet<string> ReferencedImageMarkers(IReadOnlyList<ChatItem> archiveSnapshot)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        AddReferencedImageMarkers(referenced, _transcript);
        AddReferencedImageMarkers(referenced, archiveSnapshot);
        return referenced;
    }

    private static void AddReferencedImageMarkers(
        HashSet<string> markers,
        IReadOnlyList<ChatItem> items)
    {
        foreach (var item in items)
        {
            var text = item switch
            {
                ChatMessage message => message.Text,
                ToolResult result => result.Text,
                _ => null
            };
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (Match match in ImageMarkerText.Matches(text))
            {
                markers.Add(match.Value);
            }
        }
    }

    private static IReadOnlyList<ChatItem> ReconcilePendingToolCalls(IReadOnlyList<ChatItem> items)
    {
        var completedToolCalls = new HashSet<string>(
            items.OfType<ToolResult>().Select(static result => result.CallId),
            StringComparer.Ordinal);
        var reconciled = new List<ChatItem>(items);
        foreach (var call in items.OfType<ToolCall>())
        {
            if (completedToolCalls.Contains(call.CallId))
            {
                continue;
            }

            reconciled.Add(new ToolResult(call.CallId, "Tool execution was interrupted by user.", ToolResultStatus.Failure));
            completedToolCalls.Add(call.CallId);
        }

        return reconciled;
    }

    private int NextImageNumber() =>
        ImageSnapshot().Keys.Concat(_unavailableImages.Select(static image => image.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;

    private int ReserveImageNumber()
    {
        lock (_imagesGate)
        {
            return _nextImageNumber++;
        }
    }

    private void AddImage(ImageAttachment image)
    {
        lock (_imagesGate)
        {
            _images.Add(image.Number, image);
        }
    }

    private Dictionary<int, ImageAttachment> ImageSnapshot()
    {
        lock (_imagesGate)
        {
            return new Dictionary<int, ImageAttachment>(_images);
        }
    }

    private void PruneDraftImages(string input, bool includeQueue = true)
    {
        if (_draftImages.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> queued = includeQueue ? _queue.Snapshot() : [];
        var (archiveSnapshot, archiveCount) = ArchiveSnapshotWithCount();
        if (ArchiveCount() != archiveCount)
        {
            archiveSnapshot = ArchiveSnapshot();
        }

        var images = ImageSnapshot();
        foreach (var number in _draftImages.ToArray())
        {
            var image = images[number];
            if (input.Contains(image.TrustedMarker, StringComparison.Ordinal)
                || queued.Any(text => text.Contains(image.TrustedMarker, StringComparison.Ordinal)))
            {
                continue;
            }

            _draftImages.Remove(number);
            if (!IsReferencedInTranscript(image.TrustedMarker, archiveSnapshot))
            {
                lock (_imagesGate)
                {
                    _images.Remove(number);
                }
            }
        }
    }

    private ContextCompactor CreateCompactor(IChatClient client, ReasoningOptions? reasoning = null) =>
        new(
            client,
            _settings.Retry.ToOptions(),
            attempt => Publish(new RetryScheduled(attempt)),
            () => ApplyPluginPrompt(
                CompactionPrompt.ComposeSystem(
                    CurrentPromptContext().WithMode("compaction"),
                    _placeholders,
                    _prompts.CompactionSystem),
                "compaction"),
            (phase, text) => _hooks.FinishCompaction(phase, text),
            (items, token) => _hooks.PrepareModelAsync(
                PluginModelPurpose.Compaction,
                items,
                ImageMediaTypes(),
                false,
                token),
            (response, token) => ReportModelResponseAsync(
                PluginModelPurpose.Compaction,
                response,
                token),
            (conversation, todos, previous) => CompactionPrompt.UserText(
                conversation,
                todos,
                previous,
                CurrentPromptBinding(CurrentPromptContext().WithMode("compaction")),
                _prompts.CompactionUser),
            reasoning);

    /// <summary>
    /// Collects the finished turn: records its transcript, compacts when over
    /// budget, saves the session, and publishes <see cref="TurnFinished"/>.
    /// </summary>
    public async Task FinishTurnAsync(CancellationToken cancellationToken = default)
    {
        if (_turnTask is null)
        {
            return;
        }

        TurnResult? result = null;
        string? failure = null;
        try
        {
            result = await _turnTask;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception.Message;
            Error(exception.Message);
        }
        finally
        {
            _turnActive = false;
            _turnTask = null;
            _turnSource?.Dispose();
            _turnSource = null;
        }

        var stop = result?.StopReason ?? TurnStopReason.Failed;
        var error = string.IsNullOrWhiteSpace(failure) ? _turnFault : failure;
        await _hooks.OnTurnFinishedAsync(
            new PluginTurn(
                _sessionId,
                CurrentModeName(),
                _turnUserText ?? string.Empty,
                stop.Value,
                error),
            cancellationToken);
        _turnUserText = null;
        _turnFault = null;

        if (result is null)
        {
            return;
        }

        _transcript = [.. result.Transcript];
        lock (_imagesGate)
        {
            _nextImageNumber = Math.Max(_nextImageNumber, NextImageNumber());
        }
        BindReviewConversation();
        _ledger.Record(result);

        if (result.StopReason == TurnStopReason.Completed)
        {
            try
            {
                await CompactIfNeededAsync(result, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Note("Compaction cancelled");
            }
        }

        SaveSession();
        var usage = _ledger.Usage ?? result.Usage;
        lock (_usageGate)
        {
            _shownUsage = usage;
            _shownCumulative = _ledger.CumulativeUsage;
        }

        Publish(new TurnFinished(
            result,
            usage,
            _ledger.CumulativeUsage,
            _settings.ContextWindow));
    }

    private void Publish(SessionEvent sessionEvent)
    {
        _frontEnd.Observer.OnEvent(sessionEvent);
    }

    private void Note(string text)
    {
        Publish(new NoteWritten(text));
    }

    private void Error(string text)
    {
        Publish(new ErrorWritten(text));
    }

    private void SetActivity(SessionActivity activity)
    {
        Publish(new ActivityChanged(activity));
    }

    private TokenUsage? ShownUsage()
    {
        lock (_usageGate)
        {
            return _shownUsage;
        }
    }

    private TokenUsage? ShownCumulative()
    {
        lock (_usageGate)
        {
            return _shownCumulative;
        }
    }

    private void ShowUsage(TokenUsage? usage, TokenUsage? cumulativeUsage, bool interim = false)
    {
        lock (_usageGate)
        {
            _shownUsage = usage;
            _shownCumulative = cumulativeUsage;
        }

        Publish(new UsageChanged(
            usage,
            cumulativeUsage,
            _settings.ContextWindow,
            interim));
    }

    void ITurnObserver.OnStreamEvent(ChatStreamEvent streamEvent)
    {
        Publish(new StreamReceived(streamEvent));
    }

    void ITurnObserver.OnRetry(SessionRetryAttempt attempt)
    {
        Publish(new RetryScheduled(attempt));
    }

    void ITurnObserver.OnModelRoundClosed()
    {
        Publish(new ModelRoundClosed());
    }

    void ITurnObserver.OnToolCalls(IReadOnlyList<ToolCall> calls)
    {
        Publish(new ToolCallsIssued(calls));
    }

    void ITurnObserver.OnToolResults(IReadOnlyList<ToolResult> results)
    {
        Publish(new ToolResultsReceived(results));
        PromoteAfterTools();
    }

    void ITurnObserver.OnFault(string message)
    {
        _turnFault = message;
        Error(message);
        var rawNote = _hooks.TakeRawNote();
        if (rawNote is not null)
        {
            Note(rawNote);
        }
    }

    void ITurnObserver.OnUsageUpdated(TokenUsage? contextUsage, TokenUsage? turnCumulativeUsage)
    {
        if (contextUsage is null && turnCumulativeUsage is null)
        {
            return;
        }

        TokenUsage? usage;
        TokenUsage? cumulative;
        lock (_usageGate)
        {
            usage = contextUsage ?? _shownUsage;
            cumulative = turnCumulativeUsage is not null
                ? SessionLedger.Add(_turnCumulativeBaseline, turnCumulativeUsage)
                : _shownCumulative;
        }

        ShowUsage(usage, cumulative, interim: true);
    }

    private void ShowSideQuestion()
    {
        var occupied = false;
        lock (_sideGate)
        {
            occupied = _sideRunning
                || _sideExchanges.Count > 0
                || _sidePending is not null
                || _sideFailure is not null;
        }

        if (!occupied)
        {
            Error(SideQuestion.NoneToShow);
            return;
        }

        Publish(CaptureSide(announce: true));
    }

    private void AskSide(string question)
    {
        var sideBase = SideBaseSnapshot();
        IReadOnlyList<SideExchange> prior;
        CancellationToken token;
        long generation;
        var reasoning = CurrentReasoning();
        lock (_sideGate)
        {
            _sideSource?.Cancel();
            _sideSource?.Dispose();
            _sideSource = new CancellationTokenSource();
            token = _sideSource.Token;
            generation = ++_sideGeneration;
            _sidePending = question;
            _sideLive = string.Empty;
            _sideFailure = null;
            _sideThinking = false;
            _sideRunning = true;
            prior = [.. _sideExchanges];
        }

        Publish(CaptureSide(announce: true));
        _sideTask = RunSideAsync(sideBase, prior, question, reasoning, generation, token);
    }

    private async Task<IReadOnlyList<ChatItem>?> PrepareSideConversationAsync(
        IReadOnlyList<ChatItem> sideBase,
        IReadOnlyList<SideExchange> prior,
        string question,
        ContextCompactor compactor,
        CancellationToken cancellationToken)
    {
        var conversation = SideConversation(ReconcilePendingToolCalls(sideBase));
        _ = TryMicrocompact(conversation, out conversation);
        var composed = SideQuestion.Compose(conversation, prior, question);
        if (!NeedsCompaction(composed, null))
        {
            return composed;
        }

        var outcome = await compactor.CompactAsync(
            conversation,
            _todos.Format(),
            CurrentLimits(),
            cancellationToken);
        if (outcome.Kind == CompactionKind.Applied)
        {
            conversation = outcome.Transcript;
        }

        composed = SideQuestion.Compose(conversation, prior, question);
        return NeedsCompaction(composed, null) ? null : composed;
    }

    private async Task RunSideAsync(
        IReadOnlyList<ChatItem> sideBase,
        IReadOnlyList<SideExchange> prior,
        string question,
        ReasoningOptions? reasoning,
        long generation,
        CancellationToken cancellationToken)
    {
        IStreamingChatClient? client = null;
        IStreamingChatClient? summaryClient = null;
        var owned = false;
        var ownedSummary = false;
        try
        {
            client = CreateClient(_settings);
            owned = !ReferenceEquals(client, _client);
            summaryClient = OpenSideCompactionClient(client, out ownedSummary);
            var compactor = CreateCompactor(summaryClient, CompactionReasoning());
            var conversation = await PrepareSideConversationAsync(
                sideBase,
                prior,
                question,
                compactor,
                cancellationToken);
            if (conversation is null)
            {
                FailSide(generation, SideQuestion.TooLarge);
                return;
            }

            var prepared = await _hooks.PrepareModelAsync(
                PluginModelPurpose.Side,
                conversation,
                ImageMediaTypes(),
                false,
                cancellationToken);
            var request = new ChatRequest(ImageMarkerText.ForTextModel(prepared), [], reasoning);
            var assembler = new ChatStreamAssembler();
            var live = new StringBuilder();
            await foreach (var streamEvent in client.StreamAsync(request, cancellationToken))
            {
                if (streamEvent is ChatReasoningTextDelta reasoningDelta && reasoningDelta.Text.Length > 0)
                {
                    NoteSideThinking(generation);
                }
                else if (streamEvent is ChatTextDelta text)
                {
                    live.Append(text.Text);
                    NoteSideLive(generation, live.ToString());
                }

                assembler.Apply(streamEvent);
            }

            ChatResponse response;
            try
            {
                response = assembler.ToResponse();
            }
            catch (InvalidOperationException)
            {
                FailSide(generation, SideQuestion.NoAnswer);
                return;
            }

            await ReportModelResponseAsync(PluginModelPurpose.Side, response, cancellationToken);
            if (!SideQuestion.TryReadAnswer(response, out var answer, out var ignoredToolCall))
            {
                FailSide(
                    generation,
                    ignoredToolCall ? SideQuestion.CannotUseTools : SideQuestion.NoAnswer);
                return;
            }

            RememberSide(generation, question, answer);
        }
        catch (OperationCanceledException)
        {
            FailSide(generation, SideQuestion.Cancelled);
        }
        catch (Exception exception)
        {
            FailSide(
                generation,
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Side question failed."
                    : exception.Message);
        }
        finally
        {
            if (ownedSummary)
            {
                DisposeClient(summaryClient);
            }

            if (owned)
            {
                DisposeClient(client);
            }
        }
    }

    private void NoteSideThinking(long generation)
    {
        var publish = false;
        lock (_sideGate)
        {
            if (generation == _sideGeneration && _sideRunning && !_sideThinking)
            {
                _sideThinking = true;
                publish = true;
            }
        }

        if (publish)
        {
            Publish(CaptureSide(announce: false));
        }
    }

    private void NoteSideLive(long generation, string live)
    {
        var publish = false;
        lock (_sideGate)
        {
            if (generation == _sideGeneration && _sideRunning)
            {
                _sideLive = live;
                publish = true;
            }
        }

        if (publish)
        {
            Publish(CaptureSide(announce: false));
        }
    }

    private void RememberSide(long generation, string question, string answer)
    {
        var publish = false;
        lock (_sideGate)
        {
            if (generation != _sideGeneration)
            {
                return;
            }

            _sideExchanges.Add(new SideExchange(question, answer));
            while (_sideExchanges.Count > SideQuestion.MemoryLimit)
            {
                _sideExchanges.RemoveAt(0);
            }

            _sidePending = null;
            _sideLive = string.Empty;
            _sideFailure = null;
            _sideThinking = false;
            _sideRunning = false;
            publish = true;
        }

        if (publish)
        {
            Publish(CaptureSide(announce: false));
        }
    }

    private void FailSide(long generation, string message)
    {
        var publish = false;
        lock (_sideGate)
        {
            if (generation != _sideGeneration)
            {
                return;
            }

            _sideFailure = message;
            _sideThinking = false;
            _sideRunning = false;
            publish = true;
        }

        if (publish)
        {
            Publish(CaptureSide(announce: false));
        }
    }

    private SideQuestionSnapshot CaptureSide(bool announce)
    {
        lock (_sideGate)
        {
            return new SideQuestionSnapshot(
                announce,
                [.. _sideExchanges],
                _sideRunning,
                _sidePending ?? string.Empty,
                _sideLive,
                _sideRunning ? null : _sideFailure,
                _sideThinking);
        }
    }

    private void CancelAndClearSide(bool announce)
    {
        var publish = false;
        lock (_sideGate)
        {
            var occupied = _sideRunning
                || _sideExchanges.Count > 0
                || _sidePending is not null
                || _sideFailure is not null;
            _sideSource?.Cancel();
            _sideSource?.Dispose();
            _sideSource = null;
            _sideGeneration++;
            _sideExchanges.Clear();
            _sidePending = null;
            _sideLive = string.Empty;
            _sideFailure = null;
            _sideThinking = false;
            _sideRunning = false;
            publish = announce && occupied;
        }

        if (publish)
        {
            Publish(SideQuestionSnapshot.Empty);
        }
    }

    private sealed class SlashOutput(CodingSession session) : ISlashOutput
    {
        public void WriteNote(string text)
        {
            session.Note(text);
        }

        public void WriteError(string text)
        {
            session.Error(text);
        }
    }

    private static bool StopsBusyTurn(string input) =>
        SessionCommand.TryParse(input, out var command) && StopsBusyTurn(command.Verb);

    private static bool StopsBusyTurn(SessionVerb verb) =>
        verb is SessionVerb.Quit
            or SessionVerb.Clear
            or SessionVerb.Resume
            or SessionVerb.Fork;
}
