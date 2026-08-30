using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Configuration;
using CrystalHarness.Display;
using CrystalHarness.Home;
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
    private readonly SessionRenderer _renderer;
    private readonly SessionReviewContext _reviewContext = new();
    private readonly SessionLedger _ledger = new();
    private readonly TodoList _todos = new();
    private readonly GrantStore _grants;
    private Workspace _workspace;
    private PromptSet _prompts;
    private ApprovalMode _approval;
    private bool _planMode;
    private List<ChatItem> _transcript;
    private IToolExecutor _workExecutor = null!;
    private IToolExecutor _planExecutor = null!;
    private int _idleCancels;

    private CodingSession(
        IStreamingChatClient client,
        HarnessSettings settings,
        SettingsStore settingsStore,
        CrystalHome home,
        Workspace workspace,
        SessionRenderer renderer)
    {
        _client = client;
        _settings = settings;
        _settingsStore = settingsStore;
        _promptStore = new PromptStore(home);
        _workspace = workspace;
        _renderer = renderer;
        _approval = settings.Approval;
        _grants = new GrantStore(home);
        _prompts = _promptStore.Load(workspace.Root);
        _transcript = [new ChatMessage(ChatRole.System, _prompts.WorkSystem)];
        RebuildExecutors();
    }

    public static CodingSession Create(
        IStreamingChatClient client,
        HarnessSettings settings,
        SettingsStore settingsStore,
        CrystalHome home,
        string workspaceRoot)
    {
        var renderer = new SessionRenderer();
        return new CodingSession(
            client,
            settings,
            settingsStore,
            home,
            new Workspace(workspaceRoot),
            renderer);
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        _renderer.WriteHeader(
            _settings.Model,
            _workspace.Root,
            _planMode,
            _approval);

        using var promptSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        CancellationTokenSource? turnSource = null;
        var turnActive = false;

        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            if (turnActive && turnSource is not null)
            {
                turnSource.Cancel();
                return;
            }

            _idleCancels++;
            promptSource.Cancel();
        };

        while (true)
        {
            string input;
            try
            {
                input = (await _renderer.ReadInputAsync(
                    _planMode,
                    TogglePlanFromPrompt,
                    promptSource.Token)).Trim();
                _idleCancels = 0;
            }
            catch (OperationCanceledException)
            {
                if (_idleCancels >= 2 || cancellationToken.IsCancellationRequested)
                {
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
                    return 0;
                }

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

            _renderer.WriteUser(input);
            _reviewContext.CurrentUserRequest = input;
            _transcript.Add(new ChatMessage(ChatRole.User, input));
            turnSource = new CancellationTokenSource();
            turnActive = true;
            _renderer.BeginTurn();
            try
            {
                var turn = new StreamingTurn(
                    _client,
                    _planMode ? _planExecutor : _workExecutor,
                    TurnLimits.CreateDefault(),
                    _renderer);
                var result = await turn.RunAsync(_transcript, turnSource.Token);
                _transcript = [.. result.Transcript];
                if (result.ModelCallCount > 0)
                {
                    _ledger.Record(result);
                }

                if (result.StopReason == TurnStopReason.Interrupted)
                {
                    _renderer.WriteNote("interrupted");
                }

                _renderer.WriteTurnFooter(
                    result,
                    _ledger,
                    _settings.ActiveModel.ContextWindow);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _renderer.WriteError(exception.Message);
            }
            finally
            {
                turnActive = false;
                turnSource.Dispose();
                turnSource = null;
            }
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
                _renderer.WriteHelp();
                return true;
            case SessionVerb.Plan:
                TogglePlanFromPrompt();
                _renderer.WriteNote(_planMode ? "plan" : "work");
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
                _transcript = [new ChatMessage(ChatRole.System, CurrentSystemText())];
                _ledger.Clear();
                _todos.Clear();
                _reviewContext.CurrentUserRequest = string.Empty;
                _renderer.WriteNote("new conversation");
                return true;
            case SessionVerb.Cd:
                ChangeDirectory(command.Argument);
                return true;
            case SessionVerb.Quit:
                exit = true;
                return true;
            case SessionVerb.Unknown:
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
        _renderer.WriteNote("approval  " + _approval.Value);
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
            _reviewContext);
        var options = new ToolExecutionOptions(ToolExecutionMode.Serial, 1);
        _workExecutor = new ToolExecutor(
            WorkspaceCatalog.CreateWork(_workspace, _todos, question),
            options,
            policy.DecideAsync,
            HarnessExceptionMapper.MapAsync);
        _planExecutor = new ToolExecutor(
            WorkspaceCatalog.CreatePlan(_workspace, _todos, question),
            options,
            exceptionMapper: HarnessExceptionMapper.MapAsync);
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
}
