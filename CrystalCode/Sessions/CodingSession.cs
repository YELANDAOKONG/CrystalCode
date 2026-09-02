using System.Text;
using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Approvals;
using CrystalCode.Compaction;
using CrystalCode.Configuration;
using CrystalCode.Home;
using CrystalCode.Plugins;
using CrystalCode.Prompts;
using CrystalCode.Skills;
using CrystalCode.Tools;
using CrystalCode.Tools.External;
using CrystalCode.Display.Cards;
using CrystalCode.Display.Composer;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

namespace CrystalCode.Sessions;

/// <summary>
/// Interactive Plan/Work loop. Owns chrome, catalogs, approval, and turns.
/// </summary>
public sealed class CodingSession
{
    private IStreamingChatClient _client;
    private HarnessSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentials;
    private readonly PromptStore _promptStore;
    private readonly SessionStore _sessionStore;
    private ContextCompactor _compactor;
    private readonly PluginRegistry _plugins;
    private readonly SessionRenderer _renderer;
    private readonly SessionReviewContext _reviewContext = new();
    private readonly SessionLedger _ledger = new();
    private readonly TodoList _todos = new();
    private readonly MessageQueue _queue = new();
    private readonly GrantStore _grants;
    private readonly CrystalHome _home;
    private readonly SkillDiscovery _skillDiscovery;
    private readonly bool _replayOnStart;
    private Workspace _workspace;
    private PromptSet _prompts;
    private PromptResolution _promptResolution;
    private SkillCatalog? _skills;
    private ExternalCatalog _external = ExternalCatalog.Empty;
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
    private CancellationTokenSource? _compactSource;
    private bool _turnActive;
    private int _idleCancels;

    private CodingSession(
        HarnessSettings settings,
        SettingsStore settingsStore,
        CredentialStore credentials,
        CrystalHome home,
        Workspace workspace,
        SessionRenderer renderer,
        PluginRegistry plugins,
        SessionDocument? resume)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(credentials);
        _settings = settings;
        _settingsStore = settingsStore;
        _credentials = credentials;
        _promptStore = new PromptStore(home);
        _sessionStore = new SessionStore(home);
        _workspace = workspace;
        _plugins = plugins;
        _client = CreateClient(settings);
        _renderer = renderer;
        _compactor = CreateCompactor(_client);
        _approval = settings.Approval;
        _thinkingEffort = settings.ThinkingEffort;
        _grants = new GrantStore(home);
        _home = home;
        _skillDiscovery = SkillDiscovery.Create(home);
        _promptResolution = _promptStore.Resolve(workspace.Root, settings.PromptSet);
        _prompts = _promptResolution.Prompts;
        ReloadSkills();
        _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
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
        PluginRegistry? plugins = null,
        SessionDocument? resume = null)
    {
        var renderer = new SessionRenderer();
        return new CodingSession(
            settings,
            settingsStore,
            credentials,
            home,
            new Workspace(workspaceRoot),
            renderer,
            plugins ?? PluginRegistry.CreateBuiltIn(),
            resume);
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
            DisposeClient();
        }
    }

    private async Task<int> RunLoopAsync(CancellationToken cancellationToken)
    {
        using var screen = _renderer.Open();
        _renderer.SetStatusLine(_settings.StatusLine.Enabled, _settings.StatusLine.Fields);
        _renderer.ContextWindow = _settings.ActiveModel.ContextWindow;
        _renderer.AfterTools = PromoteAfterTools;
        RefreshSlashCommands();
        _renderer.ShowEstimatedTokens = _settings.EstimatedTokens;
        _renderer.VerboseTools = _settings.VerboseTools;
        _renderer.VerboseCommands = _settings.VerboseCommands;
        _renderer.OnVerboseToggled = PersistVerboseToggle;
        _renderer.WriteHeader(
            _settings.Model,
            _workspace.Root,
            _planMode,
            _approval,
            CurrentThinkingStatus(),
            CurrentPromptStatus());
        if (_replayOnStart)
        {
            PresentResume();
        }

        WritePromptNotes();
        if (!string.IsNullOrWhiteSpace(CurrentPromptStatus()))
        {
            _renderer.WriteNote("Prompt set  " + _promptResolution.PromptSet);
        }

        ReloadExternalToolsWithProgress();
        RebuildExecutors();
        WriteExternalNotes();
        ShowTodos();

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

            if (_compactSource is not null)
            {
                _compactSource.Cancel();
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
                    await FinishTurnAsync(promptSource.Token);
                    return 0;
                }

                if (!promptSource.IsCancellationRequested)
                {
                    continue;
                }

                _renderer.WriteNote("Ctrl+C again to exit");
                if (!promptSource.TryReset())
                {
                    await FinishTurnAsync(promptSource.Token);
                    return 0;
                }

                continue;
            }

            if (read.TurnEnded)
            {
                await FinishTurnAsync(promptSource.Token);
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
                        await FinishTurnAsync(promptSource.Token);
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
                await FinishTurnAsync(promptSource.Token);
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
            try
            {
                await CompactForcedAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _renderer.WriteNote("Compaction cancelled");
            }

            StartTurnIfQueued();
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
            case SessionVerb.StatusLine:
                ChangeStatusLine(command.Argument);
                return (true, false);
            case SessionVerb.Clear:
                BeginNewSession();
                _renderer.ClearConversation();
                _renderer.ShowUsage(null, null);
                _renderer.WriteNote("New conversation");
                return (true, false);
            case SessionVerb.Cd:
                ChangeDirectory(command.Argument);
                return (true, false);
            case SessionVerb.Resume:
                ResumeSession(command.Argument);
                return (true, false);
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
            case SessionVerb.Export:
                ExportSession(command.Argument);
                return (true, false);
            case SessionVerb.Quit:
                return (true, true);
            case SessionVerb.Unknown:
                if (_plugins.TryExecute(command.Argument, _renderer))
                {
                    return (true, false);
                }

                _renderer.WriteError("Unknown command  " + command.Argument);
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
        _renderer.WriteNote("Approval  " + ApprovalLabel.For(_approval));
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
        _renderer.WriteNote("Thinking  " + ThinkingLabel.For(_thinkingEffort));
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
            _renderer.WriteError("Estimated tokens is on or off.");
            return;
        }

        _settingsStore.Save(_settings);
        _renderer.ShowEstimatedTokens = _settings.EstimatedTokens;
        _renderer.WriteNote(
            "Estimated tokens  " + (_settings.EstimatedTokens ? "On" : "Off"));
    }

    private void ChangeVerbose(string argument)
    {
        if (!VerboseChangeArguments.TryParse(argument, out var target, out var enabled, out var error))
        {
            _renderer.WriteError(error);
            return;
        }

        if (target is null)
        {
            _renderer.WriteNote(
                "Verbose tools  " + (_settings.VerboseTools ? "On" : "Off")
                + "  ·  Verbose commands  "
                + (_settings.VerboseCommands ? "On" : "Off"));
            return;
        }

        switch (target.Value)
        {
            case VerboseChangeArguments.Target.Tools:
                _settings = enabled is bool toolsEnabled
                    ? _settings.WithVerboseTools(toolsEnabled)
                    : _settings.WithVerboseTools(!_settings.VerboseTools);
                _renderer.VerboseTools = _settings.VerboseTools;
                _renderer.WriteNote(
                    "Verbose tools  " + (_settings.VerboseTools ? "On" : "Off"));
                break;
            case VerboseChangeArguments.Target.Commands:
                _settings = enabled is bool commandsEnabled
                    ? _settings.WithVerboseCommands(commandsEnabled)
                    : _settings.WithVerboseCommands(!_settings.VerboseCommands);
                _renderer.VerboseCommands = _settings.VerboseCommands;
                _renderer.WriteNote(
                    "Verbose commands  " + (_settings.VerboseCommands ? "On" : "Off"));
                break;
            default:
                return;
        }

        _settingsStore.Save(_settings);
    }

    private void PersistVerboseToggle(DisplayInput.VerboseToggle toggle)
    {
        _settings = toggle switch
        {
            DisplayInput.VerboseToggle.Tools => _settings.WithVerboseTools(_renderer.VerboseTools),
            DisplayInput.VerboseToggle.Commands => _settings.WithVerboseCommands(_renderer.VerboseCommands),
            _ => _settings
        };
        _settingsStore.Save(_settings);
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
            _renderer.WriteError("Finish the current turn before switching models.");
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            _renderer.WriteNote(
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
            _renderer.WriteError(resolveError);
            return;
        }

        if (selection.Provider == _settings.Provider
            && string.Equals(selection.Model, _settings.Model, StringComparison.Ordinal))
        {
            _renderer.WriteNote("Model  " + selection);
            return;
        }

        var nextSettings = _settings.WithSelection(selection.Provider, selection.Model);
        if (!_credentials.TryResolve(
                nextSettings.ActiveProvider,
                out var apiKey,
                out var credentialError))
        {
            _renderer.WriteError(credentialError);
            return;
        }

        IStreamingChatClient nextClient;
        try
        {
            nextClient = ChatClientFactory.Create(nextSettings, apiKey, _plugins);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _renderer.WriteError(exception.Message);
            return;
        }

        var previous = _client;
        _client = nextClient;
        _compactor = CreateCompactor(nextClient);
        _settings = nextSettings;
        _settingsStore.Save(_settings);
        DisposeClient(previous);
        ReplaceLiveSystem();
        RebuildExecutors();
        _renderer.ContextWindow = _settings.ActiveModel.ContextWindow;
        RefreshSlashCommands();
        RefreshChrome();
        _renderer.WriteNote("Model  " + selection);
    }

    private void ExportSession(string argument)
    {
        if (_turnActive)
        {
            _renderer.WriteError("Finish the current turn before exporting.");
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
            _renderer.WriteError(exception.Message);
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
                _renderer.WriteError(parseError);
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
            _renderer.WriteError(error);
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
            _renderer.WriteError(error);
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
            _renderer.WriteError("Finish the current turn before exporting prompts.");
            return;
        }

        string directory;
        if (string.IsNullOrWhiteSpace(explicitDirectory))
        {
            if (!TryResolveExportDirectory(out var exportRoot, out var resolveError))
            {
                _renderer.WriteError(resolveError);
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
            _renderer.WriteError(error);
            return;
        }

        if (!ExportFilesystem.TryEnsureDirectory(directory, out var ensureError))
        {
            _renderer.WriteError(ensureError);
            return;
        }

        try
        {
            PromptTemplateExport.Write(directory);
            _renderer.WriteNote("Exported prompts  " + directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _renderer.WriteError("Export failed  " + exception.Message);
        }
    }

    private void WriteExportUsage()
    {
        if (!TryResolveExportDirectory(out var directory, out var error))
        {
            _renderer.WriteError(error);
            return;
        }

        _renderer.WriteNote(ExportText.Usage(_sessionId, directory));
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
                _renderer.WriteError(ensureError);
                return;
            }

            File.WriteAllText(path, contents, Encoding.UTF8);
            _renderer.WriteNote("Exported  " + path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            _renderer.WriteError("Export failed  " + exception.Message);
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
            _renderer.WriteError(exception.Message);
            return;
        }

        if (tokens.Count > 0
            && string.Equals(tokens[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Count > 2)
            {
                _renderer.WriteError("Prompt export accepts at most one directory.");
                return;
            }

            ExportPromptTemplates(tokens.Count > 1 ? tokens[1] : null);
            return;
        }

        if (tokens.Count == 0)
        {
            _renderer.WriteNote(PromptSelectionText.Format(_promptResolution));
            return;
        }

        if (_turnActive)
        {
            _renderer.WriteError("Finish the current turn before switching prompt sets.");
            return;
        }

        if (!PromptSetChangeArguments.TryParseName(tokens, out var requested, out var parseError))
        {
            _renderer.WriteError(parseError);
            return;
        }
        var resolution = _promptStore.Resolve(_workspace.Root, requested);
        if (!string.Equals(requested, PromptSetNames.Default, StringComparison.Ordinal)
            && !string.Equals(requested, resolution.PromptSet, StringComparison.Ordinal))
        {
            _renderer.WriteError("Prompt set not found  " + requested);
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
        _renderer.WriteNote("Prompt set  " + resolution.PromptSet);
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
            _renderer.WriteNote(ToolListText.FormatApproval(_settings.ExternalToolApproval));
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

    private void ChangeToolApproval(string source, IReadOnlyList<string> parts)
    {
        if (parts.Count == 1)
        {
            var currentPolicy = source == "home"
                ? _settings.ExternalToolApproval.Home
                : _settings.ExternalToolApproval.Project;
            _renderer.WriteNote($"Tool approval  {Title(source)} {Title(currentPolicy.Value)}");
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
            _renderer.WriteError(exception.Message);
            return;
        }

        if (_turnActive)
        {
            _renderer.WriteError("Finish the current turn before changing tool approval.");
            return;
        }

        var approval = source == "home"
            ? _settings.ExternalToolApproval.WithHome(policy)
            : _settings.ExternalToolApproval.WithProject(policy);
        _settings = _settings.WithExternalToolApproval(approval);
        _settingsStore.Save(_settings);
        ReloadExternalToolsWithProgress();
        RebuildExecutors();
        _renderer.WriteNote($"Tool approval  {Title(source)} {Title(policy.Value)}");
    }

    private void ChangeToolLoading(string command)
    {
        if (_turnActive)
        {
            _renderer.WriteError("Finish the current turn before reloading tools.");
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
        _renderer.WriteNote(command == "reload" ? "Tools reloaded" : "External tools  " + Title(command));
    }

    private void ShowTools()
    {
        var fallback = ToolListText.Format(
            _planExecutor.Definitions,
            _workExecutor.Definitions,
            _external,
            _settings);
        var widget = ToolListWidget.Create(
            _planExecutor.Definitions,
            _workExecutor.Definitions,
            _external,
            _settings);
        _renderer.WriteNote(widget, fallback);
    }

    private void ShowStatus(string argument)
    {
        var full = string.Equals(argument, "full", StringComparison.OrdinalIgnoreCase);
        if (argument.Length > 0 && !full)
        {
            _renderer.WriteError("Status command must be /status or /status full.");
            return;
        }

        _renderer.WriteStatus(
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
                PlanTools: _planExecutor.Definitions.Count,
                WorkTools: _workExecutor.Definitions.Count,
                ExternalTools: _external.Tools.Count,
                CumulativeUsage: _ledger.CumulativeUsage,
                CustomStatusLineEnabled: _settings.StatusLine.Enabled),
            full);
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
            _renderer.WriteError(exception.Message);
            return;
        }

        if (tokens.Count == 0)
        {
            _renderer.WriteNote(
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
            _renderer.WriteError(exception.Message);
            return;
        }

        _settings = _settings.WithStatusLine(statusLine);
        _settingsStore.Save(_settings);
        _renderer.SetStatusLine(statusLine.Enabled, statusLine.Fields);
        _renderer.WriteNote("Custom status line  " + (statusLine.Enabled ? "On" : "Off"));
    }

    private void WriteToolsUsage()
    {
        _renderer.WriteError(
            "Tools command must be /tools, /tools approval, /tools on|off|reload, "
            + "/tools home author|host, or /tools project author|host.");
    }

    private static string Title(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private IStreamingChatClient CreateClient(HarnessSettings settings)
    {
        if (!_credentials.TryResolve(settings.ActiveProvider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return ChatClientFactory.Create(settings, apiKey, _plugins);
    }

    private void DisposeClient() => DisposeClient(_client);

    private static void DisposeClient(IStreamingChatClient client)
    {
        (client as IDisposable)?.Dispose();
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
            ReloadSkills();
            ReloadExternalToolsWithProgress();
            ReloadPrompts();
            RebuildExecutors();
            WriteExternalNotes();
            RefreshChrome();
            _renderer.WriteNote("Workspace  " + _workspace.Root);
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

    private PromptContext CurrentPromptContext() =>
        PromptContext.Create(
            _workspace.Root,
            _settings.Provider.Value,
            _settings.Model,
            _planMode ? "plan" : "work",
            _skills is null ? string.Empty : SkillGuidance.Render(_skills),
            _prompts.Instructions);

    private string CurrentSystemText() =>
        _planMode
            ? _prompts.ComposePlan(CurrentPromptContext())
            : _prompts.ComposeWork(CurrentPromptContext());

    private string CurrentReviewSystemText() =>
        _prompts.ComposeReview(CurrentPromptContext().WithMode("review"));

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
            _renderer.WriteError("Finish the current turn before compacting.");
            return;
        }

        await RunCompactionAsync(
            _transcript,
            silentSkip: false,
            pumpFrame: true,
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
            pumpFrame: true,
            cancellationToken);
    }

    private async Task<CompactionOutcome> CompactRoundAsync(
        IReadOnlyList<ChatItem> transcript,
        CancellationToken cancellationToken)
    {
        _reviewContext.Conversation = transcript;
        if (!NeedsCompaction(transcript, _renderer.LastUsage))
        {
            return new CompactionOutcome(transcript, CompactionKind.Unchanged);
        }

        var outcome = await RunCompactionAsync(
            transcript,
            silentSkip: true,
            pumpFrame: false,
            cancellationToken);
        if (outcome.Kind == CompactionKind.Unchanged)
        {
            _renderer.WriteError("Session is too large to compact.");
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
        bool pumpFrame,
        CancellationToken cancellationToken)
    {
        _renderer.WriteNote("Compacting context...");
        _renderer.SetProgress(ProgressText.Compacting);
        CompactionOutcome outcome;
        using var compactSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _compactSource = compactSource;
        var compactTask = _compactor.CompactAsync(
            transcript,
            _todos.Format(),
            CurrentLimits(),
            compactSource.Token);
        try
        {
            if (pumpFrame)
            {
                await _renderer.PumpUntilAsync(
                    compactTask,
                    Enqueue,
                    _planMode,
                    TogglePlanFromPrompt,
                    compactSource.Token);
            }

            outcome = await compactTask;
        }
        finally
        {
            if (!compactTask.IsCompleted)
            {
                compactSource.Cancel();
                try
                {
                    await compactTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _compactSource = null;
            _renderer.SetProgress(_turnActive ? ProgressText.WaitingForModel : string.Empty);
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
                _renderer.ShowContextUsage(new TokenUsage(input, 0));
            }

            _renderer.WriteNote("Compacted context");
            return outcome;
        }

        if (outcome.Kind == CompactionKind.Exhausted)
        {
            _renderer.WriteError("Session is too large to compact.");
            return outcome;
        }

        if (!silentSkip)
        {
            _renderer.WriteNote("Compaction skipped");
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
        _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
        _ledger.Clear();
        _todos.Clear();
        BindReviewConversation();
        _sessionId = SessionStore.NewId();
        _sessionCreatedUtc = DateTimeOffset.UtcNow;
        SaveSession();
        ShowTodos();
    }

    private void ResumeSession(string argument)
    {
        if (!SessionResume.TryLoad(
                _sessionStore,
                _workspace.Root,
                argument,
                out var document,
                out var error))
        {
            _renderer.WriteError(error);
            return;
        }

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
                _renderer.WriteError("Session is empty");
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
            _renderer.WriteError(error);
            return;
        }

        var sourceId = source.Id!;
        var fork = SessionFork.Create(
            source,
            SessionStore.NewId(),
            _workspace.Root,
            DateTimeOffset.UtcNow);
        ApplyDocument(fork);
        SaveSession();
        RefreshChrome();
        _renderer.ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
        _renderer.WriteHistory(_transcript);
        ShowTodos();
        _renderer.WriteNote($"Forked  {sourceId}  ->  {_sessionId}");
    }

    private void ShowSessions(string argument)
    {
        var includeAll = string.Equals(argument, "all", StringComparison.OrdinalIgnoreCase);
        if (!includeAll && !string.IsNullOrWhiteSpace(argument))
        {
            _renderer.WriteError("Usage: /sessions [all]");
            return;
        }

        var sessions = _sessionStore.List(includeAll ? null : _workspace.Root);
        if (sessions.Count == 0)
        {
            _renderer.WriteNote(includeAll ? "No sessions" : "No sessions for this workspace");
            return;
        }

        _renderer.WriteNote(SessionListText.Format(sessions, _sessionId, includeAll));
    }

    private void ApplyDocument(SessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var items = TranscriptCodec.Read(document.Items);
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
            SessionMapper.ReadUsage(document.Usage),
            SessionMapper.ReadUsage(document.CumulativeUsage));
        _queue.Clear();
        BindReviewConversation();
        RebuildExecutors();
    }

    private void PresentResume()
    {
        RefreshChrome();
        _renderer.ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
        _renderer.WriteHistory(_transcript);
        _renderer.WriteNote("Resumed  " + _sessionId);
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
        _renderer.ShowUsage(_ledger.Usage, _ledger.CumulativeUsage);
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
            CurrentReviewSystemText(),
            CurrentReasoning());
        var policy = new ApprovalPolicy(
            _approval,
            _workspace,
            _grants,
            approvalPrompt,
            reviewer,
            _reviewContext,
            [.. _plugins.Classifiers, _external.Classifier],
            _skills,
            _external.AutomaticTools);
        var options = new ToolExecutionOptions(ToolExecutionMode.Serial, 1);
        _workExecutor = new ToolExecutor(
            WorkspaceCatalog.CreateWork(
                _workspace,
                _todos,
                question,
                _plugins,
                _skills,
                _external),
            options,
            policy.DecideAsync,
            HarnessExceptionMapper.MapAsync);
        _planExecutor = new ToolExecutor(
            WorkspaceCatalog.CreatePlan(
                _workspace,
                _todos,
                question,
                _plugins,
                _skills,
                _external),
            options,
            policy.DecideAsync,
            HarnessExceptionMapper.MapAsync);
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
            _settings.ExternalToolApproval);
    }

    private void ReloadExternalToolsWithProgress()
    {
        if (_settings.ExternalTools)
        {
            _renderer.SetProgress(ProgressText.LoadingTools);
        }

        try
        {
            ReloadExternalTools();
        }
        finally
        {
            if (_settings.ExternalTools)
            {
                _renderer.SetProgress(string.Empty);
            }
        }
    }

    private void WriteExternalNotes()
    {
        foreach (var note in _external.Notes)
        {
            _renderer.WriteNote(note);
        }
    }

    private void BindReviewConversation()
    {
        _reviewContext.Conversation = _transcript;
    }

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
            _renderer.WriteNote(note);
        }
    }

    private void RefreshSlashCommands()
    {
        _renderer.SetSlashCommands(
            _plugins.Commands,
            ThinkingCompletions.For(_settings.ActiveModel),
            ModelCompletions.For(_settings.Catalog, _settings.Provider),
            PromptSetCompletions.For(_promptResolution),
            ToolCompletions.All,
            ExportCompletions.All);
    }

    private void RefreshChrome()
    {
        _renderer.SetChrome(
            _planMode,
            _approval,
            CurrentThinkingStatus(),
            _settings.Model,
            _workspace.Root,
            CurrentPromptStatus());
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
        var items = new List<TodoBarItem>();
        foreach (var todo in _todos.Snapshot())
        {
            items.Add(new TodoBarItem(TodoList.StatusMark(todo.Status), todo.Content));
        }

        _renderer.SetTodos(items);
    }

    private void ShowTodoList()
    {
        _renderer.WriteNote(_todos.Format());
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
            CompactRoundAsync,
            SessionRetryOptions.Default);
        return turn.RunAsync(_transcript, cancellationToken);
    }

    private ContextCompactor CreateCompactor(IChatClient client) =>
        new(
            client,
            SessionRetryOptions.Default,
            _renderer.OnRetry,
            () => CompactionPrompt.ComposeSystem(CurrentPromptContext().WithMode("compaction")));

    private async Task FinishTurnAsync(CancellationToken cancellationToken = default)
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
        BindReviewConversation();
        if (result.ModelCallCount > 0)
        {
            _ledger.Record(result);
        }

        if (result.StopReason == TurnStopReason.Completed)
        {
            try
            {
                await CompactIfNeededAsync(result, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _renderer.WriteNote("Compaction cancelled");
            }
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
            && command.Verb is SessionVerb.Quit
                or SessionVerb.Clear
                or SessionVerb.Resume
                or SessionVerb.Fork;
    }
}
