using System.Text;
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
    private IStreamingMultimodalChatClient? _multimodalClient;
    private HarnessSettings _settings;
    private readonly SettingsStore _settingsStore;
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
    private PluginHookPipeline _hooks = PluginHookPipeline.Empty;
    private bool _pluginSessionOpen;
    private readonly SessionToolHost _toolHost;
    private ApprovalMode _approval;
    private ThinkingSelection _thinkingEffort;
    private bool _planMode;
    private List<ChatItem> _transcript;
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
    private bool _turnActive;
    private readonly object _sideGate = new();
    private readonly List<SideExchange> _sideExchanges = [];
    private CancellationTokenSource? _sideSource;
    private Task? _sideTask;
    private long _sideGeneration;
    private bool _sideRunning;
    private string? _sidePending;
    private string _sideLive = string.Empty;
    private string? _sideFailure;
    private TokenUsage? _shownUsage;
    private TokenUsage? _shownCumulative;
    private TokenUsage? _turnCumulativeBaseline;
    private bool _promptHistoryAvailable = true;

    private CodingSession(
        HarnessSettings settings,
        SettingsStore settingsStore,
        CredentialStore credentials,
        CrystalHome home,
        Workspace workspace,
        SessionFrontEnd frontEnd,
        PluginRegistry plugins,
        SessionDocument? resume)
    {
        ArgumentNullException.ThrowIfNull(frontEnd);
        ArgumentNullException.ThrowIfNull(frontEnd.Trust);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(credentials);
        _settings = settings;
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
            () => _sessionId ?? string.Empty,
            () => _approval.Value);
        _thinkingEffort = settings.ThinkingEffort;
        _grants = new GrantStore(home);
        _trust = new WorkspaceTrustStore(home);
        _home = home;
        _skillDiscovery = SkillDiscovery.Create(home);
        ReloadPlugins();
        _client = CreateClient(settings);
        _multimodalClient = CreateMultimodalClient(settings);
        _compactor = CreateCompactor(_client);
        _promptResolution = _promptStore.Resolve(workspace.Root, settings.PromptSet);
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
    }

    public static CodingSession Create(
        HarnessSettings settings,
        SettingsStore settingsStore,
        CredentialStore credentials,
        CrystalHome home,
        string workspaceRoot,
        SessionFrontEnd frontEnd,
        PluginRegistry? plugins = null,
        SessionDocument? resume = null)
    {
        return new CodingSession(
            settings,
            settingsStore,
            credentials,
            home,
            new Workspace(workspaceRoot),
            frontEnd,
            plugins ?? PluginRegistry.CreateBuiltIn(),
            resume);
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

        ReloadExternalToolsWithProgress();
        RebuildExecutors();
        ReplaceLiveSystem();
        WritePluginNotes();
        WriteExternalNotes();
        await OpenPluginSessionAsync(cancellationToken);
        ShowTodos();
    }

    /// <summary>
    /// Handles one submitted prompt: a slash command, a follow-up while a turn
    /// runs, an interrupt (empty text while working), or the start of a turn.
    /// Returns true when the operator asked to quit.
    /// </summary>
    public async Task<bool> SubmitAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        await SavePromptHistoryAsync(text, cancellationToken);
        var input = text.Trim();
        PruneDraftImages(input);
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

                Enqueue(input);
                return false;
            }

            _turnSource?.Cancel();
            await FinishTurnAsync(cancellationToken);
            StartTurnIfQueued();
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

        StartTurn(input);
        return false;
    }

    /// <summary>
    /// Collects a finished turn, then starts the queued follow-up if any.
    /// </summary>
    public async Task CompleteTurnAsync(CancellationToken cancellationToken)
    {
        await FinishTurnAsync(cancellationToken);
        StartTurnIfQueued();
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
        _settings = target switch
        {
            VerboseTarget.Tools => _settings.WithVerboseTools(enabled),
            VerboseTarget.Commands => _settings.WithVerboseCommands(enabled),
            VerboseTarget.Approvals => _settings.WithVerboseApprovals(enabled),
            VerboseTarget.Thinking => _settings.WithVerboseThinking(enabled),
            _ => _settings
        };
        _settingsStore.Save(_settings);
        PublishPreferences();
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
            try
            {
                await CompactForcedAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Note("Compaction cancelled");
            }

            StartTurnIfQueued();
            PruneDraftImages(string.Empty);
            return (true, false);
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
                BeginNewSession();
                Publish(new ConversationCleared());
                ShowUsage(null, null);
                Note("New conversation");
                return (true, false);
            case SessionVerb.Cd:
                await ChangeDirectoryAsync(command.Argument, cancellationToken);
                return (true, false);
            case SessionVerb.Trust:
                return (true, await ChangeTrustAsync(command.Argument, cancellationToken));
            case SessionVerb.Fork:
                ForkSession(command.Argument);
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

        _settings = _settings.WithApproval(_approval);
        _settingsStore.Save(_settings);
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

        HarnessSettings updated;
        try
        {
            updated = _settings.WithApprovalModel(next);
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
        _settingsStore.Save(_settings);
        ReplaceApprovalClient(next, client);
        RebuildExecutors();
        Note(next.Describe());
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

        settings = new ApprovalModelSettings(true, selection.Provider.Value, selection.Model);
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
        if (client is null)
        {
            return;
        }

        _approvalClient = client;
        _approvalClientKey = ApprovalClientKey(settings);
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

        _settings = _settings.WithThinkingEffort(_thinkingEffort);
        _settingsStore.Save(_settings);
        RebuildExecutors();
        RefreshChrome();
        Note("Thinking  " + ThinkingLabel.For(_thinkingEffort));
    }

    private void ChangeEstimatedTokens(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            _settings = _settings.WithEstimatedTokens(!_settings.EstimatedTokens);
        }
        else if (TryParseToggle(argument, out var enabled))
        {
            _settings = _settings.WithEstimatedTokens(enabled);
        }
        else
        {
            Error("Estimated tokens is on or off.");
            return;
        }

        _settingsStore.Save(_settings);
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
                + "  ·  Verbose approvals  "
                + (_settings.VerboseApprovals ? "On" : "Off")
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
            case VerboseTarget.Approvals:
                SetVerbose(
                    VerboseTarget.Approvals,
                    enabled is bool approvalsEnabled ? approvalsEnabled : !_settings.VerboseApprovals);
                Note("Verbose approvals  " + (_settings.VerboseApprovals ? "On" : "Off"));
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
        _compactor = CreateCompactor(nextClient);
        _settings = nextSettings;
        _settingsStore.Save(_settings);
        if (!ReferenceEquals(previous, _approvalClient))
        {
            DisposeClient(previous);
        }

        DisposeClient(previousMultimodal);
        ReplaceLiveSystem();
        RebuildExecutors();
        RefreshSlashCommands();
        RefreshChrome();
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
        var items = TranscriptExport.ConversationItems(_transcript);
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
        document.Items = TranscriptCodec.Write(TranscriptExport.ConversationItems(_transcript));
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

        if (!PromptSetChangeArguments.TryParseName(tokens, out var requested, out var parseError))
        {
            Error(parseError);
            return;
        }
        var resolution = _promptStore.Resolve(_workspace.Root, requested);
        if (!string.Equals(requested, PromptSetNames.Default, StringComparison.Ordinal)
            && !string.Equals(requested, resolution.PromptSet, StringComparison.Ordinal))
        {
            Error("Prompt set not found  " + requested);
            return;
        }

        _settings = _settings.WithPromptSet(resolution.PromptSet);
        _settingsStore.Save(_settings);
        _promptResolution = resolution;
        _prompts = resolution.Prompts;
        ReplaceLiveSystem();
        RebuildExecutors();
        RefreshSlashCommands();
        RefreshChrome();
        WritePromptNotes();
        Note("Prompt set  " + resolution.PromptSet);
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
            ChangeToolApproval(command, parts);
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
        _settings = _settings.WithExternalToolApproval(approval);
        _settingsStore.Save(_settings);
        ReloadExternalToolsWithProgress();
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
            _settings = _settings.WithExternalTools(command == "on");
            _settingsStore.Save(_settings);
        }

        ReloadExternalToolsWithProgress();
        RebuildExecutors();
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
                ContextWindow: _settings.ActiveModel.ContextWindow,
                Usage: _ledger.Usage,
                UserTurns: _ledger.UserTurns,
                ModelCalls: _ledger.ModelCalls,
                ToolCalls: _ledger.ToolCalls,
                QueuedMessages: _queue.Count,
                Todos: _todos.Count,
                SkillsEnabled: _settings.Skills,
                ExternalToolsEnabled: _settings.ExternalTools,
                EstimatedTokensEnabled: _settings.EstimatedTokens,
                VerboseToolsEnabled: _settings.VerboseTools,
                VerboseCommandsEnabled: _settings.VerboseCommands,
                VerboseApprovalsEnabled: _settings.VerboseApprovals,
                VerboseThinkingEnabled: _settings.VerboseThinking,
                PlanTools: planToolCount,
                WorkTools: workToolCount,
                ExternalTools: _external.Tools.Count,
                PluginsEnabled: _settings.Plugins,
                Plugins: _loadedPlugins.Plugins.Count,
                CumulativeUsage: _ledger.CumulativeUsage,
                CustomStatusLineEnabled: _settings.StatusLine.Enabled,
                ApprovalModel: _settings.ApprovalModel.Enabled
                    ? _settings.ApprovalModel.Provider + " / " + _settings.ApprovalModel.Model
                    : null),
            full));
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
            Note(
                "Custom status line  " + (_settings.StatusLine.Enabled ? "On" : "Off")
                + "\nFields  " + string.Join(' ', _settings.StatusLine.Fields)
                + "\nAvailable  " + string.Join(' ', StatusLineSettings.AvailableFields));
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
                _ => _settings.StatusLine.WithFields(tokens)
            };
        }
        catch (ArgumentException exception)
        {
            Error(exception.Message);
            return;
        }

        _settings = _settings.WithStatusLine(statusLine);
        _settingsStore.Save(_settings);
        PublishPreferences();
        Note("Custom status line  " + (statusLine.Enabled ? "On" : "Off"));
    }

    private void WriteToolsUsage()
    {
        Error(
            "Tools command must be /tools, /tools approval, /tools on|off|reload, "
            + "/tools home author|host, or /tools project author|host.");
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
        _compactor = CreateCompactor(_client);
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
        ReleaseApprovalClient();
        DisposeClient(_client);
        DisposeClient(_multimodalClient);
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

        if (!_workspace.TryResolve(argument, out var candidate, out var error))
        {
            Error(error);
            return;
        }

        if (!await ConfirmWorkspaceAsync(candidate, cancellationToken))
        {
            Note("Staying in " + _workspace.Root);
            return;
        }

        _workspace.SetRoot(candidate);
        await ClosePluginSessionAsync(cancellationToken);
        ReloadSkills();
        ReloadPluginsWithProgress();
        ReloadExternalToolsWithProgress();
        ReloadPrompts();
        RebuildExecutors();
        WritePluginNotes();
        WriteExternalNotes();
        await OpenPluginSessionAsync(cancellationToken);
        RefreshChrome();
        Note("Workspace  " + _workspace.Root);
    }

    private async Task<bool> ChangeTrustAsync(string argument, CancellationToken cancellationToken)
    {
        var command = argument.Trim().ToLowerInvariant();
        var trustRoot = GitRoot.TrustRoot(_workspace.Root);
        if (command.Length == 0)
        {
            Note("Workspace trust  " + (_settings.WorkspaceTrust ? "on" : "off"));
            Note("Trust root  " + trustRoot);
            Note("Trusted  " + (_trust.Contains(trustRoot) ? "yes" : "no"));
            return false;
        }

        if (command is not ("on" or "off" or "forget"))
        {
            Error("Trust command must be /trust, /trust on|off, or /trust forget.");
            return false;
        }

        if (command == "forget")
        {
            _trust.Forget(trustRoot);
            Note("Forgot  " + trustRoot);
            return false;
        }

        var enabled = command == "on";
        _settings = _settings.WithWorkspaceTrust(enabled);
        _settingsStore.Save(_settings);
        Note("Workspace trust  " + (enabled ? "on" : "off"));
        if (!enabled || _trust.Contains(trustRoot))
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

        _trust.Remember(trustRoot);
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

        var trustRoot = GitRoot.TrustRoot(workspaceRoot);
        if (_trust.Contains(trustRoot))
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

        _trust.Remember(trustRoot);
        return true;
    }

    private PromptContext CurrentPromptContext() =>
        PromptContext.Create(
            _workspace.Root,
            _settings.Provider.Value,
            _settings.Model,
            _planMode ? "plan" : "work",
            _skills is null ? string.Empty : SkillGuidance.Render(_skills),
            _prompts.Instructions,
            sessionId: _sessionId,
            approval: _approval.Value);

    private string CurrentSystemText() =>
        AppendPluginPrompt(
            _planMode
                ? _prompts.ComposePlan(CurrentPromptContext())
                : _prompts.ComposeWork(CurrentPromptContext()),
            _planMode ? "plan" : "work");

    private string CurrentReviewSystemText() =>
        AppendPluginPrompt(
            _prompts.ComposeReview(CurrentPromptContext().WithMode("review")),
            "review");

    private string AppendPluginPrompt(string text, string mode)
    {
        var extra = _hooks.AppendPrompt(mode, _prompts.Instructions);
        return extra.Length == 0 ? text : text + "\n\n" + extra;
    }

    private void ReloadPrompts()
    {
        _promptResolution = _promptStore.Resolve(_workspace.Root, _settings.PromptSet);
        _prompts = _promptResolution.Prompts;
        ReplaceLiveSystem();
        WritePromptNotes();
    }

    private CompactionLimits CurrentLimits() =>
        new(
            _settings.ActiveModel.ContextWindow,
            _settings.ActiveModel.MaxTokens,
            _settings.CompactionThreshold);

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
            cancellationToken);
    }

    private async Task CompactIfNeededAsync(TurnResult result, CancellationToken cancellationToken)
    {
        if (!NeedsCompaction(_transcript, result.Usage ?? _ledger.Usage))
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
        _reviewContext.Conversation = transcript;
        if (!NeedsCompaction(transcript, ShownUsage()))
        {
            return new CompactionOutcome(transcript, CompactionKind.Unchanged);
        }

        var outcome = await RunCompactionAsync(
            transcript,
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
        CancellationToken cancellationToken)
    {
        Note("Compacting context...");
        SetActivity(SessionActivity.Compacting);
        CompactionOutcome outcome;
        using var compactSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _compactSource = compactSource;
        try
        {
            outcome = await _compactor.CompactAsync(
                transcript,
                _todos.Format(),
                CurrentLimits(),
                compactSource.Token);
        }
        finally
        {
            _compactSource = null;
            SetActivity(_turnActive ? SessionActivity.WaitingForModel : SessionActivity.Idle);
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
            return outcome;
        }

        if (outcome.Kind == CompactionKind.Exhausted)
        {
            Error("Session is too large to compact.");
            return outcome;
        }

        if (!silentSkip)
        {
            Note("Compaction skipped");
        }

        return outcome;
    }

    private void SaveSession()
    {
        _sessionStore.Save(CreateDocument());
    }

    private SessionDocument CreateDocument() =>
        new()
        {
            Id = _sessionId,
            Workspace = _workspace.Root,
            PlanMode = _planMode,
            CreatedUtc = _sessionCreatedUtc,
            Items = TranscriptCodec.Write(_transcript),
            ImageMarkersTagged = true,
            Images =
            [
                .. SessionMapper.WriteImages(ImageSnapshot().Values.Where(IsReferencedInTranscript)),
                .. _unavailableImages.Where(image => IsReferencedInTranscript(
                    ImageMarkerText.Tag(image.Number)))
            ],
            Todos = SessionMapper.WriteTodos(_todos.Snapshot()),
            UserTurns = _ledger.UserTurns,
            ModelCalls = _ledger.ModelCalls,
            ToolCalls = _ledger.ToolCalls,
            Usage = SessionMapper.WriteUsage(_ledger.Usage),
            CumulativeUsage = SessionMapper.WriteUsage(_ledger.CumulativeUsage)
        };

    private void BeginNewSession()
    {
        DiscardQueue();
        CancelAndClearSide(announce: true);
        Publish(new ImageHistoryInvalidated());
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
        _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
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
        SaveSession();
        ShowTodos();
    }

    private async Task ResumeSessionAsync(
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (HasConversation())
            {
                SaveSession();
            }

            var available = _sessionStore.List(_workspace.Root);
            if (available.Count == 0)
            {
                Error("No session for this workspace");
                return;
            }

            var chosen = await _frontEnd.Sessions.ChooseAsync(
                available,
                _sessionId,
                cancellationToken);
            if (chosen is null)
            {
                return;
            }

            argument = chosen;
        }

        if (!SessionResume.TryLoad(
                _sessionStore,
                _workspace.Root,
                argument,
                out var document,
                out var error))
        {
            Error(error);
            return;
        }

        CancelAndClearSide(announce: true);
        ApplyDocument(document);
        DiscardQueue();
        PresentResume();
    }

    private void ForkSession(string argument)
    {
        SessionDocument source;
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (!HasConversation())
            {
                Error("Session is empty");
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

        CancelAndClearSide(announce: true);
        var sourceId = source.Id!;
        var fork = SessionFork.Create(
            source,
            SessionStore.NewId(),
            _workspace.Root,
            DateTimeOffset.UtcNow);
        ApplyDocument(fork);
        SaveSession();
        RefreshChrome();
        ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
        Publish(new HistoryReplayed([.. _transcript]));
        ShowTodos();
        Note($"Forked  {sourceId}  ->  {_sessionId}");
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
        Publish(new ImageHistoryInvalidated());
        var items = TranscriptCodec.Read(document.Items);
        _sessionId = document.Id!;
        _sessionCreatedUtc = document.CreatedUtc ?? DateTimeOffset.UtcNow;
        _planMode = document.PlanMode;
        _transcript = document.ImageMarkersTagged
            ? items
            : ImageMarkerText.TagLegacy(
                items,
                document.Images.Where(static image => image is not null)
                    .Select(static image => image.Number)
                    .ToHashSet());
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
        Publish(new HistoryReplayed([.. _transcript]));
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

    private bool HasConversation() => TranscriptCodec.HasConversation(_transcript);

    private void RebuildExecutors()
    {
        var approvalPrompt = _frontEnd.Approvals;
        var question = _frontEnd.Questions;
        var reviewer = new ModelApprovalReviewer(
            ReviewerClient(),
            CurrentReviewSystemText(),
            ReviewerReasoning());
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
            (call, classification) => _hooks.Advise(call, classification));
        var options = new ToolExecutionOptions(ToolExecutionMode.Serial, 1);
        var workExecutor = new ToolExecutor(
            WorkspaceCatalog.CreateWork(
                _workspace,
                _todos,
                question,
                _plugins,
                _skills,
                _external,
                _settings.BashTimeoutSeconds,
                _loadedPlugins),
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
                _loadedPlugins),
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
        _loadedPlugins = PluginCatalog.Load(_home, _workspace, _settings.Plugins);
        _hooks = new PluginHookPipeline(_loadedPlugins.Hooks, Note);
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
        var command = argument.Trim().ToLowerInvariant();
        if (command.Length == 0)
        {
            foreach (var line in _loadedPlugins.Describe(_settings.Plugins))
            {
                Note(line);
            }

            return;
        }

        if (command is not ("on" or "off" or "reload"))
        {
            Error("Plugins command must be /plugins, /plugins on|off|reload.");
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
            _settings = _settings.WithPlugins(command == "on");
            _settingsStore.Save(_settings);
        }

        ReloadPluginsWithProgress();
        ReloadExternalToolsWithProgress();
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
        return ThinkingSelection.Default.ToReasoningOptions(model);
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

    private static string ApprovalClientKey(ApprovalModelSettings settings) =>
        settings.Provider + "\n" + settings.Model;

    private ReasoningOptions? CurrentReasoning() =>
        _thinkingEffort.ToReasoningOptions(_settings.ActiveModel);

    private string CurrentThinkingStatus() =>
        ThinkingStatus.For(_settings.ActiveModel, _thinkingEffort);

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
            ToolCompletions.All,
            ExportCompletions.All);
        Publish(new SlashCommandsChanged(menu));
    }

    private SessionChrome CurrentChrome() =>
        new(
            _settings.Model,
            _workspace.Root,
            _planMode,
            _approval,
            CurrentThinkingStatus(),
            CurrentPromptStatus());

    private void RefreshChrome()
    {
        Publish(new ChromeChanged(CurrentChrome()));
    }

    private SessionPreferences CurrentPreferences() =>
        new(
            _settings.EstimatedTokens,
            _settings.VerboseTools,
            _settings.VerboseCommands,
            _settings.VerboseApprovals,
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
        if (_queue.Count > 0)
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
        Publish(new QueueChanged(_queue.Snapshot()));
    }

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
    /// </summary>
    public void Enqueue(string input)
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
        var message = AttachPendingImages(input);
        Publish(new UserMessageSent(message));
        _transcript.Add(new ChatMessage(ChatRole.User, message));
        var images = ImageSnapshot();
        _draftImages.RemoveWhere(number => message.Contains(
            images[number].TrustedMarker,
            StringComparison.Ordinal));
        _turnSource = new CancellationTokenSource();
        _turnActive = true;
        lock (_usageGate)
        {
            _turnCumulativeBaseline = _shownCumulative;
        }

        Publish(new TurnStarted());
        _turnTask = ExecuteTurnAsync(_turnSource.Token);
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
                SessionRetryOptions.Default,
                ReserveImageNumber,
                AddImage,
                ImageSnapshot);
            return multimodalTurn.RunAsync(_transcript, cancellationToken);
        }

        var turn = new StreamingTurn(
            _client,
            _planMode ? _planExecutor : _workExecutor,
            _settings.ExecutionBudget,
            this,
            CurrentReasoning(),
            CompactRoundAsync,
            SessionRetryOptions.Default);
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

    private bool IsReferencedInTranscript(ImageAttachment image) =>
        IsReferencedInTranscript(image.TrustedMarker);

    private bool IsReferencedInTranscript(string marker) =>
        _transcript.Any(item => item switch
        {
            ChatMessage message => message.Text.Contains(marker, StringComparison.Ordinal),
            ToolResult result => result.Text.Contains(marker, StringComparison.Ordinal),
            _ => false
        });

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
            if (!IsReferencedInTranscript(image))
            {
                lock (_imagesGate)
                {
                    _images.Remove(number);
                }
            }
        }
    }

    private ContextCompactor CreateCompactor(IChatClient client) =>
        new(
            client,
            SessionRetryOptions.Default,
            attempt => Publish(new RetryScheduled(attempt)),
            () => AppendPluginPrompt(
                CompactionPrompt.ComposeSystem(CurrentPromptContext().WithMode("compaction")),
                "compaction"),
            (phase, text) => _hooks.AppendCompaction(phase, text));

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
        try
        {
            result = await _turnTask;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Error(exception.Message);
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
            _settings.ActiveModel.ContextWindow));
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
            _settings.ActiveModel.ContextWindow,
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
        Error(message);
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
        var transcript = ImageMarkerText.ForTextModel(_transcript).ToArray();
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
            _sideRunning = true;
            prior = [.. _sideExchanges];
        }

        Publish(CaptureSide(announce: true));
        _sideTask = RunSideAsync(transcript, prior, question, reasoning, generation, token);
    }

    private async Task RunSideAsync(
        IReadOnlyList<ChatItem> transcript,
        IReadOnlyList<SideExchange> prior,
        string question,
        ReasoningOptions? reasoning,
        long generation,
        CancellationToken cancellationToken)
    {
        IStreamingChatClient? client = null;
        var owned = false;
        try
        {
            client = CreateClient(_settings);
            owned = !ReferenceEquals(client, _client);
            var request = new ChatRequest(
                SideQuestion.Compose(transcript, prior, question),
                [],
                reasoning);
            var assembler = new ChatStreamAssembler();
            var live = new StringBuilder();
            await foreach (var streamEvent in client.StreamAsync(request, cancellationToken))
            {
                if (streamEvent is ChatTextDelta text)
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
            if (owned)
            {
                DisposeClient(client);
            }
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
                _sideRunning ? null : _sideFailure);
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

    private static bool StopsBusyTurn(string input)
    {
        return SessionCommand.TryParse(input, out var command)
            && command.Verb is SessionVerb.Quit
                or SessionVerb.Clear
                or SessionVerb.Resume
                or SessionVerb.Fork;
    }
}
