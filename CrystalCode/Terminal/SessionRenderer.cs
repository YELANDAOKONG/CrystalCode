using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Rendering;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Display.Cards;
using CrystalCode.Display.Composer;
using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Display.Transcript;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Terminal;

/// <summary>
/// Fullscreen session shell. Alternate buffer, not AnsiConsole.Live.
/// </summary>
public sealed class SessionRenderer : IDisposable
{
    private const int PollMilliseconds = 40;
    private const int EscapeHoldMilliseconds = 50;
    private static readonly TimeSpan PaintBudget = TimeSpan.FromMilliseconds(33);
    private readonly object _gate = new();
    private readonly TranscriptLog _log = new();
    private readonly ComposerBuffer _composer = new();
    private readonly ShellChrome _chrome = new();
    private readonly List<string> _modalOverlay = [];
    private readonly List<string> _queueItems = [];
    private readonly List<TodoBarItem> _todoItems = [];
    private IRenderable? _overlayWidget;
    private readonly List<SlashOption> _slashOptions = [];
    private readonly ScreenPainter _painter = new();
    private readonly ScrollAnchor _scrollAnchor = new();
    private readonly InputDecoder _decoder = new();
    private AlternateScreen? _screen;
    private SlashPicker? _picker;
    private string? _streamKind;
    private readonly StreamToolNames _toolNames = new();
    private readonly Dictionary<string, string> _toolCallNames = new(StringComparer.Ordinal);
    private Stopwatch? _turnClock;
    private DateTimeOffset _lastPaint;
    private int _scrollBack;
    private int _paintedWidth;
    private int _paintedHeight;
    private bool _composerPaused;
    private bool _showEstimatedTokens;
    private int _streamedCharacters;
    private TokenUsage? _lastUsage;
    private TokenUsage? _cumulativeUsage;
    private DateTimeOffset? _retryUntil;
    private int _retryAttempt;
    private bool _imagePasteRequested;
    private bool _fullPageOverlay;
    private int _pageScroll;
    private const int MaxSideRows = 12;
    private SideQuestionSnapshot? _side;
    private bool _sideOpen;
    private int _sideIndex;
    private int _sideScroll;
    private bool _sideStick = true;
    private bool _sideClearRequested;
    private bool _sideCancelRequested;
    private bool _sideSuspended;
    private int _sideSpinnerFrame;
    private DateTimeOffset _sideSpinnerAt;

    public int ContextWindow { get; set; }

    public Action<DisplayInput.VerboseToggle>? OnVerboseToggled { get; set; }

    public Func<CancellationToken, Task<string?>>? OnImagePasteAsync { get; set; }

    public Action<string>? OnComposerEdited { get; set; }

    public Action? OnSideCleared { get; set; }

    public Action? OnSideCancelled { get; set; }

    internal bool SideQuestionOpen
    {
        get
        {
            lock (_gate)
            {
                return _sideOpen;
            }
        }
    }

    internal bool OverlayVisible
    {
        get
        {
            lock (_gate)
            {
                return _overlayWidget is not null || _modalOverlay.Count > 0;
            }
        }
    }

    public bool ShowEstimatedTokens
    {
        get
        {
            lock (_gate)
            {
                return _showEstimatedTokens;
            }
        }
        set
        {
            lock (_gate)
            {
                _showEstimatedTokens = value;
                RefreshTokenEstimateUnlocked();
                PaintUnlocked(force: true);
            }
        }
    }

    public bool VerboseTools
    {
        get
        {
            lock (_gate)
            {
                return _log.VerboseTools;
            }
        }
        set
        {
            lock (_gate)
            {
                _log.VerboseTools = value;
                PaintUnlocked(force: true);
            }
        }
    }

    public bool VerboseCommands
    {
        get
        {
            lock (_gate)
            {
                return _log.VerboseCommands;
            }
        }
        set
        {
            lock (_gate)
            {
                _log.VerboseCommands = value;
                PaintUnlocked(force: true);
            }
        }
    }

    public bool VerboseApprovals
    {
        get
        {
            lock (_gate)
            {
                return _log.VerboseApprovals;
            }
        }
        set
        {
            lock (_gate)
            {
                _log.VerboseApprovals = value;
                PaintUnlocked(force: true);
            }
        }
    }

    public bool VerboseThinking
    {
        get
        {
            lock (_gate)
            {
                return _log.VerboseThinking;
            }
        }
        set
        {
            lock (_gate)
            {
                _log.VerboseThinking = value;
                PaintUnlocked(force: true);
            }
        }
    }

    internal string ChromeWorkspaceRoot
    {
        get
        {
            lock (_gate)
            {
                return _chrome.WorkspaceRoot;
            }
        }
    }

    internal string ChromeProgress
    {
        get
        {
            lock (_gate)
            {
                return _chrome.Progress;
            }
        }
    }

    public TokenUsage? LastUsage
    {
        get
        {
            lock (_gate)
            {
                return _lastUsage;
            }
        }
    }

    internal string ChromeUsage
    {
        get
        {
            lock (_gate)
            {
                return _chrome.Usage;
            }
        }
    }

    internal string ChromeUsageTotal
    {
        get
        {
            lock (_gate)
            {
                return _chrome.UsageTotal;
            }
        }
    }

    internal string ChromeTokenEstimate
    {
        get
        {
            lock (_gate)
            {
                return _chrome.TokenEstimate;
            }
        }
    }

    public IDisposable Open()
    {
        lock (_gate)
        {
            _screen?.Dispose();
            _painter.Clear();
            _decoder.Reset();
            _screen = AlternateScreen.TryEnter();
            PaintUnlocked(force: true);
        }

        return this;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _screen?.Dispose();
            _screen = null;
            _painter.Clear();
            _decoder.Reset();
        }
    }

    public void SetSlashCommands(IReadOnlyList<SlashOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            _slashOptions.Clear();
            _slashOptions.AddRange(options);
            _picker = null;
        }
    }

    public void WriteHeader(
        string model,
        string workspaceRoot,
        bool planMode,
        ApprovalMode approval,
        string thinking = "",
        string promptSet = "")
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _chrome.Model = model;
            _chrome.WorkspaceRoot = workspaceRoot;
            _chrome.PlanMode = planMode;
            _chrome.Approval = ApprovalLabel.For(approval);
            _chrome.Thinking = thinking;
            _chrome.PromptSet = promptSet;
            _composer.PlanMode = planMode;
            if (Framed)
            {
                PaintUnlocked(force: true);
                return;
            }

            var mode = ModeLabel.For(planMode);
            var modeColor = planMode ? Theme.Plan : Theme.Work;
            var thinkingText = string.IsNullOrWhiteSpace(thinking)
                ? string.Empty
                : $"  ·  {MarkupText.Escape(thinking)}";
            var promptText = string.IsNullOrWhiteSpace(promptSet)
                ? string.Empty
                : $"  ·  Prompt {MarkupText.Escape(promptSet)}";
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]{MarkupText.Escape(model)}  ·  [/]"
                + $"[{modeColor}]{mode}[/]"
                + $"[{Theme.Chrome}]  ·  {MarkupText.Escape(ApprovalLabel.For(approval))}"
                + $"{thinkingText}{promptText}  ·  "
                + $"{MarkupText.Escape(PathDisplay.Shorten(workspaceRoot))}[/]");
            AnsiConsole.WriteLine();
        }
    }

    public void SetChrome(
        bool planMode,
        ApprovalMode approval,
        string thinking = "",
        string? model = null,
        string? workspaceRoot = null,
        string? promptSet = null)
    {
        lock (_gate)
        {
            _chrome.PlanMode = planMode;
            _composer.PlanMode = planMode;
            _chrome.Approval = ApprovalLabel.For(approval);
            _chrome.Thinking = thinking;
            if (promptSet is not null)
            {
                _chrome.PromptSet = promptSet;
            }
            if (!string.IsNullOrWhiteSpace(model))
            {
                _chrome.Model = model;
            }

            if (!string.IsNullOrWhiteSpace(workspaceRoot))
            {
                _chrome.WorkspaceRoot = workspaceRoot;
            }

            PaintUnlocked(force: true);
        }
    }

    public void SetStatusLine(bool enabled, IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        lock (_gate)
        {
            _chrome.CustomStatusLine = enabled;
            _chrome.StatusLineFields = [.. fields];
            PaintUnlocked(force: true);
        }
    }

    public void WriteUser(string text)
    {
        Add(TranscriptKind.User, text);
    }

    public void WriteNote(string text)
    {
        Add(TranscriptKind.Note, text);
    }

    internal void WriteNote(IRenderable widget, string fallbackText)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(fallbackText);
        lock (_gate)
        {
            CommitLiveUnlocked();
            _log.Add(TranscriptKind.Note, fallbackText, widget);
            if (!Framed)
            {
                AnsiConsole.Write(widget);
                AnsiConsole.WriteLine();
            }

            PaintUnlocked(force: true);
        }
    }

    public void WriteError(string text)
    {
        Add(TranscriptKind.Error, text);
    }

    public void WriteApprovalPass(IRenderable card)
    {
        ArgumentNullException.ThrowIfNull(card);
        lock (_gate)
        {
            CommitLiveUnlocked();
            _log.Add(TranscriptKind.Approval, string.Empty, card);
            if (!Framed && _log.VerboseApprovals)
            {
                AnsiConsole.Write(card);
                AnsiConsole.WriteLine();
            }

            PaintUnlocked(force: true);
        }
    }

    public void WriteHelp(IReadOnlyList<ISlashCommand>? extras = null)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            AddHelpUnlocked(
                "enter        Submit; queue while working",
                "empty enter  Send the queue now",
                "queue        Stays above the composer; sends after this tool or turn",
                "ctrl+j       Newline",
                "\\ enter      Newline",
                "tab          Plan / Work, or complete / and arguments",
                "shift+tab    Plan / Work",
                "?            Shortcuts when empty",
                "ctrl+o       Toggle verbose tool results",
                "ctrl+g       Toggle verbose command output",
                "pageup       Scroll transcript (also pagedown and wheel)",
                "up/down      Move cursor; at edge, browse prompt history",
                "shift+drag   Select terminal text (option or fn on some macOS terminals)");
            foreach (var spec in SlashCatalog.BuiltIn)
            {
                var names = "/" + spec.Name;
                if (spec.Aliases.Count > 0)
                {
                    names += "  " + string.Join("  ", spec.Aliases.Select(alias => "/" + alias));
                }

                AddHelpUnlocked($"{names,-28}{spec.Help}");
            }

            AddHelpUnlocked("ctrl+c      Stop turn; at idle clears input, twice on empty exits");
            if (extras is not null)
            {
                foreach (var command in extras)
                {
                    var help = string.IsNullOrWhiteSpace(command.Help)
                        ? command.Name
                        : command.Help;
                    AddHelpUnlocked($"/{command.Name,-27}{help}");
                }
            }

            PaintUnlocked(force: true);
        }
    }

    internal void WriteStatus(SessionStatus status, bool full)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            CommitLiveUnlocked();
            _chrome.WorkspaceRoot = status.WorkspaceRoot;
            _chrome.PlanMode = status.PlanMode;
            _chrome.Approval = ApprovalLabel.For(status.Approval);
            _chrome.Thinking = status.Thinking;
            _chrome.PromptSet = status.PromptSet == PromptSetNames.Default
                ? string.Empty
                : status.PromptSet;
            ApplyUsageUnlocked(status.Usage, status.CumulativeUsage, status.ContextWindow);
            var text = StatusText.Format(status, full);
            var widget = StatusWidget.Create(status, full);
            _log.Add(TranscriptKind.Note, text, widget);
            if (!Framed)
            {
                AnsiConsole.Write(widget);
                AnsiConsole.WriteLine();
            }
            PaintUnlocked(force: true);
        }
    }

    internal void ShowStatsPage(IRenderable page)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            CommitLiveUnlocked();
            _fullPageOverlay = true;
            _pageScroll = 0;
            _modalOverlay.Clear();
            _overlayWidget = page;
            PaintUnlocked(force: true);
        }
    }

    internal void ShowSideQuestion(SideQuestionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _side = snapshot;
            if (SideQuestionWidget.IsEmpty(snapshot))
            {
                _sideOpen = false;
                _sideSuspended = false;
                _sideIndex = 0;
                _sideScroll = 0;
                PaintUnlocked(force: true);
                return;
            }

            if (_composerPaused)
            {
                if (snapshot.Announce)
                {
                    _sideSuspended = true;
                }

                return;
            }

            if (_fullPageOverlay)
            {
                return;
            }

            if (snapshot.Announce || _sideOpen)
            {
                if (snapshot.Announce)
                {
                    _sideIndex = SideQuestionWidget.LatestSlot(snapshot);
                    _sideScroll = 0;
                    _sideStick = true;
                }
                else
                {
                    var last = Math.Max(0, SideQuestionWidget.SlotCount(snapshot) - 1);
                    _sideIndex = Math.Clamp(_sideIndex, 0, last);
                }

                _sideOpen = true;
            }

            PaintUnlocked(force: true);
        }
    }

    internal void DismissSideQuestion()
    {
        lock (_gate)
        {
            if (!_sideOpen)
            {
                return;
            }

            _sideOpen = false;
            PaintUnlocked(force: true);
        }
    }

    public void WriteTurnFooter(
        TurnResult result,
        TokenUsage? usage,
        TokenUsage? cumulativeUsage,
        int contextWindow)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            ApplyUsageUnlocked(
                usage ?? result.Usage,
                cumulativeUsage,
                contextWindow);
            _chrome.ToolCount = result.ToolCallCount;
            _chrome.Elapsed = _turnClock is null
                ? string.Empty
                : UsageText.FormatElapsed(_turnClock.Elapsed);
            _chrome.Activity = string.Empty;
            _chrome.Progress = string.Empty;
            if (result.StopReason != TurnStopReason.Completed)
            {
                var stopReason = DisplayCase.Token(result.StopReason.Value);
                _log.Add(TranscriptKind.Note, stopReason);
                WriteFallback(TranscriptKind.Note, stopReason);
            }

            PaintUnlocked(force: true);
        }
    }

    public void ClearConversation()
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _log.Clear();
            _scrollBack = 0;
            _scrollAnchor.Reset();
            PaintUnlocked(force: true);
        }
    }

    public void SeedPromptHistory(IEnumerable<string> entries)
    {
        lock (_gate)
        {
            _composer.SeedHistory(entries);
        }
    }

    public void ForgetImageHistory()
    {
        lock (_gate)
        {
            _composer.ForgetImageHistory();
        }
    }

    public void WriteHistory(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            CommitLiveUnlocked();
            _log.Clear();
            _scrollBack = 0;
            _scrollAnchor.Reset();
            foreach (var line in TranscriptReplay.Lines(items))
            {
                _log.Add(line.Kind, line.Text, toolName: line.ToolName);
                WriteFallback(line.Kind, line.Text, line.ToolName);
            }

            PaintUnlocked(force: true);
        }
    }

    public void BeginTurn()
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _turnClock = Stopwatch.StartNew();
            _toolNames.Clear();
            _toolCallNames.Clear();
            _streamedCharacters = 0;
            SetTurnActivityUnlocked("Running", ProgressText.WaitingForModel);
            _chrome.ToolCount = 0;
            _chrome.Elapsed = string.Empty;
            PaintUnlocked(force: true);
        }
    }

    public void OnStreamEvent(ChatStreamEvent streamEvent)
    {
        lock (_gate)
        {
            switch (streamEvent)
            {
                case ChatReasoningTextDelta reasoning when reasoning.Text.Length > 0:
                    OpenLiveUnlocked(TranscriptKind.Thinking);
                    var reasoningText = _log.AppendLive(TranscriptKind.Thinking, reasoning.Text);
                    _streamedCharacters += reasoning.Text.Length;
                    SetTurnActivityUnlocked("Thinking", ProgressText.Thinking);
                    if (_log.VerboseThinking && reasoningText.Length > 0)
                    {
                        WriteFallbackDelta(TranscriptKind.Thinking, reasoningText);
                    }
                    PaintUnlocked(force: false);
                    break;
                case ChatTextDelta text when text.Text.Length > 0:
                    OpenLiveUnlocked(TranscriptKind.Assistant);
                    var assistantText = _log.AppendLive(TranscriptKind.Assistant, text.Text);
                    _streamedCharacters += text.Text.Length;
                    SetTurnActivityUnlocked("Writing", ProgressText.Writing);
                    if (assistantText.Length > 0)
                    {
                        WriteFallbackDelta(TranscriptKind.Assistant, assistantText);
                    }
                    PaintUnlocked(force: false);
                    break;
                case ChatToolCallDelta toolCall:
                    if (toolCall.NameDelta.Length > 0)
                    {
                        var name = _toolNames.Apply(
                            toolCall.CandidateIndex,
                            toolCall.ItemIndex,
                            toolCall.NameDelta);
                        SetTurnActivityUnlocked(
                            DisplayCase.Token(name),
                            ProgressText.Calling(name));
                    }

                    PaintUnlocked(force: false);
                    break;
                default:
                    break;
            }
        }
    }

    public void OnRetry(SessionRetryAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_gate)
        {
            DiscardLiveUnlocked();
            _streamedCharacters = 0;
            _retryUntil = DateTimeOffset.UtcNow + attempt.Delay;
            _retryAttempt = attempt.Attempt;
            SetTurnActivityUnlocked("Retrying", ProgressText.Retrying(attempt.Attempt, attempt.Delay));
            var note = "Retrying model request  " + attempt.Message;
            _log.Add(TranscriptKind.Note, note);
            WriteFallback(TranscriptKind.Note, note);
            PaintUnlocked(force: true);
        }
    }

    public void OnModelRoundClosed()
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            PaintUnlocked(force: true);
        }
    }

    public void OnToolCalls(IReadOnlyList<ToolCall> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        lock (_gate)
        {
            CommitLiveUnlocked();
            foreach (var call in calls)
            {
                var text = ToolCallText.Summary(call.Name, call.Arguments);
                _toolCallNames[call.CallId] = call.Name;
                _log.Add(TranscriptKind.Tool, text);
                WriteFallback(TranscriptKind.Tool, text);
            }

            if (calls.Count > 0)
            {
                SetTurnActivityUnlocked(
                    DisplayCase.Token(calls[0].Name),
                    ProgressText.Running(calls[0].Name));
                PaintUnlocked(force: true);
            }
        }
    }

    public void OnToolResults(IReadOnlyList<ToolResult> results)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            foreach (var result in results)
            {
                var body = ToolResultText.Body(result.Text);
                var kind = result.Status == ToolResultStatus.Success
                    ? TranscriptKind.Result
                    : TranscriptKind.Error;
                var toolName = _toolCallNames.TryGetValue(result.CallId, out var name)
                    ? name
                    : null;
                _toolCallNames.Remove(result.CallId);
                _log.Add(kind, body, toolName: toolName);
                WriteFallback(kind, body, toolName);
            }

            _chrome.ToolCount += results.Count;
            _streamedCharacters = 0;
            SetTurnActivityUnlocked("Running", ProgressText.WaitingForModel);
            PaintUnlocked(force: true);
        }
    }

    public void ShowUsage(TokenUsage? usage, TokenUsage? cumulativeUsage)
    {
        lock (_gate)
        {
            ApplyUsageUnlocked(usage, cumulativeUsage, ContextWindow);
            PaintUnlocked(force: true);
        }
    }

    public void ShowUsage(TokenUsage? usage)
    {
        ShowUsage(usage, usage);
    }

    /// <summary>
    /// Applies usage that arrives while a turn streams. Painting is throttled.
    /// </summary>
    internal void ShowLiveUsage(TokenUsage? usage, TokenUsage? cumulativeUsage)
    {
        lock (_gate)
        {
            ApplyUsageUnlocked(usage, cumulativeUsage, ContextWindow);
            PaintUnlocked(force: false);
        }
    }

    public void CloseStream()
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            PaintUnlocked(force: true);
        }
    }

    public void SetOverlay(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        lock (_gate)
        {
            _fullPageOverlay = false;
            _pageScroll = 0;
            _sideOpen = false;
            _overlayWidget = null;
            _modalOverlay.Clear();
            _modalOverlay.AddRange(lines);
            PaintUnlocked(force: true);
        }
    }

    public void SetOverlay(IRenderable widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        lock (_gate)
        {
            _fullPageOverlay = false;
            _pageScroll = 0;
            _sideOpen = false;
            _modalOverlay.Clear();
            _overlayWidget = widget;
            PaintUnlocked(force: true);
        }
    }

    public void ClearOverlay()
    {
        lock (_gate)
        {
            CloseFullPageUnlocked();
            PaintUnlocked(force: true);
        }
    }

    public void SetProgress(string progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_gate)
        {
            if (!IsRetryCaption(progress))
            {
                ClearRetryUnlocked();
            }

            _chrome.Progress = progress;
            PaintUnlocked(force: true);
        }
    }

    public async Task PumpUntilAsync(
        Task wake,
        Action<string>? onSubmit,
        bool planMode,
        Func<bool> togglePlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(togglePlan);
        lock (_gate)
        {
            _composer.PlanMode = planMode;
            _chrome.PlanMode = planMode;
            RefreshPickerUnlocked();
            PaintUnlocked(force: true);
        }

        while (!wake.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var burst = await ReadAvailableKeysAsync(
                wake,
                ignorePause: false,
                cancellationToken);
            if (burst is null)
            {
                break;
            }

            var submitted = await DispatchBurstAsync(burst, togglePlan, cancellationToken);

            if (submitted is not null)
            {
                onSubmit?.Invoke(submitted.Trim());
            }
        }

        await wake;
    }

    public void PauseComposer()
    {
        lock (_gate)
        {
            if (!_composerPaused)
            {
                _sideSuspended = _sideOpen;
            }

            _composerPaused = true;
            _sideOpen = false;
        }
    }

    public void ResumeComposer()
    {
        lock (_gate)
        {
            _composerPaused = false;
            if (_sideSuspended
                && _side is not null
                && !SideQuestionWidget.IsEmpty(_side)
                && !_fullPageOverlay)
            {
                _sideOpen = true;
            }

            _sideSuspended = false;
            PaintUnlocked(force: true);
        }
    }

    public void SetQueue(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            _queueItems.Clear();
            _queueItems.AddRange(items.Select(ImageMarkerText.Display));
            _chrome.Queued = _queueItems.Count;
            PaintUnlocked(force: true);
        }
    }

    public void SetTodos(IReadOnlyList<TodoBarItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            _todoItems.Clear();
            _todoItems.AddRange(items);
            PaintUnlocked(force: true);
        }
    }

    public void SeedComposer(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            _composer.Insert(text);
            RefreshPickerUnlocked();
            PaintUnlocked(force: true);
        }
    }

    public bool TryClearComposer()
    {
        lock (_gate)
        {
            if (_fullPageOverlay)
            {
                CloseFullPageUnlocked();
                PaintUnlocked(force: true);
                return true;
            }

            if (_composer.IsEmpty)
            {
                return false;
            }

            _composer.Clear();
            OnComposerEdited?.Invoke(_composer.SubmissionText);
            _picker = null;
            PaintUnlocked(force: true);
            return true;
        }
    }

    public async Task<string> ReadInputAsync(
        bool planMode,
        Func<bool> togglePlan,
        CancellationToken cancellationToken)
    {
        var read = await ReadPromptAsync(
            planMode,
            togglePlan,
            wake: null,
            preserveStream: false,
            ignorePause: true,
            cancellationToken);
        return read.Text;
    }

    public async Task<PromptRead> ReadPromptAsync(
        bool planMode,
        Func<bool> togglePlan,
        Task? wake,
        bool preserveStream,
        bool ignorePause,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!preserveStream)
            {
                CommitLiveUnlocked();
            }

            _composer.PlanMode = planMode;
            _chrome.PlanMode = planMode;
            RefreshPickerUnlocked();
            PaintUnlocked(force: true);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var burst = await ReadAvailableKeysAsync(
                wake,
                ignorePause,
                cancellationToken);
            if (burst is null)
            {
                return PromptRead.Ended;
            }

            var submitted = await DispatchBurstAsync(burst, togglePlan, cancellationToken);

            if (submitted is not null)
            {
                return PromptRead.Submitted(submitted);
            }
        }
    }

    internal async Task<string?> DispatchBurstAsync(
        IReadOnlyList<ConsoleKeyInfo> burst,
        Func<bool> togglePlan,
        CancellationToken cancellationToken,
        bool checkSize = true)
    {
        IReadOnlyList<IInputEvent> events;
        lock (_gate)
        {
            events = _decoder.Push(burst);
        }

        var index = 0;
        while (index < events.Count)
        {
            string? submitted;
            bool pasteImage;
            bool clearSide;
            bool cancelSide;
            lock (_gate)
            {
                var pageRows = Math.Max(1, CurrentRegions().TranscriptRows - 1);
                submitted = null;
                pasteImage = false;
                clearSide = false;
                cancelSide = false;
                while (index < events.Count)
                {
                    submitted = DispatchUnlocked(events[index], pageRows, togglePlan, checkSize);
                    index++;
                    pasteImage = _imagePasteRequested;
                    _imagePasteRequested = false;
                    clearSide = _sideClearRequested;
                    _sideClearRequested = false;
                    cancelSide = _sideCancelRequested;
                    _sideCancelRequested = false;
                    if (submitted is not null || pasteImage || clearSide || cancelSide)
                    {
                        break;
                    }
                }

                RefreshPickerUnlocked();
                PaintUnlocked(force: true);
            }

            if (clearSide)
            {
                OnSideCleared?.Invoke();
            }

            if (cancelSide)
            {
                OnSideCancelled?.Invoke();
            }

            if (pasteImage && OnImagePasteAsync is not null)
            {
                var marker = await OnImagePasteAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(marker))
                {
                    lock (_gate)
                    {
                        _composer.InsertAtomic(marker);
                        RefreshPickerUnlocked();
                        PaintUnlocked(force: true);
                    }
                }
            }

            if (submitted is not null)
            {
                return submitted;
            }
        }

        return null;
    }

    public Task<InputKey> ReadKeyAsync(CancellationToken cancellationToken) =>
        ReadKeyAsync(scrollPlainArrows: true, cancellationToken);

    internal async Task<InputKey> ReadKeyAsync(
        bool scrollPlainArrows,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var burst = await ReadAvailableKeysAsync(
                wake: null,
                ignorePause: true,
                cancellationToken);
            if (burst is null)
            {
                continue;
            }

            InputKey? mapped = null;
            lock (_gate)
            {
                var pageRows = Math.Max(1, CurrentRegions().TranscriptRows - 1);
                var dirty = false;
                foreach (var item in _decoder.Push(burst))
                {
                    switch (item)
                    {
                        case InputPaste:
                            dirty = true;
                            break;
                        case InputWheel wheel:
                            _scrollBack = Math.Max(0, _scrollBack + wheel.Delta);
                            dirty = true;
                            break;
                        case InputKey key:
                            if (TryReadKeyScroll(
                                key,
                                scrollPlainArrows,
                                pageRows,
                                out var delta))
                            {
                                _scrollBack = Math.Max(0, _scrollBack + delta);
                                dirty = true;
                                break;
                            }

                            mapped = key;
                            break;
                        default:
                            break;
                    }

                    if (mapped is not null)
                    {
                        break;
                    }
                }

                if (dirty)
                {
                    PaintUnlocked(force: true);
                }
            }

            if (mapped is { } chosen)
            {
                return chosen;
            }
        }
    }

    internal static bool TryReadKeyScroll(
        InputKey key,
        bool scrollPlainArrows,
        int pageRows,
        out int delta) =>
        ScrollInput.TryKeyScroll(
            key,
            scrollPlainArrows,
            pageRows,
            out delta);

    internal async Task<string?> ReadOverlayInputAsync(
        string initialText,
        Func<string, int, IRenderable> widget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialText);
        ArgumentNullException.ThrowIfNull(widget);
        var buffer = new ComposerBuffer();
        buffer.Replace(initialText);
        SetOverlay(widget(buffer.Text, buffer.Cursor));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var burst = await ReadAvailableKeysAsync(
                wake: null,
                ignorePause: true,
                cancellationToken);
            if (burst is null)
            {
                continue;
            }

            string? submitted = null;
            var canceled = false;
            lock (_gate)
            {
                var pageRows = Math.Max(1, CurrentRegions().TranscriptRows - 1);
                foreach (var item in _decoder.Push(burst))
                {
                    switch (item)
                    {
                        case InputPaste paste:
                            buffer.Insert(paste.Text);
                            break;
                        case InputWheel wheel:
                            _scrollBack = Math.Max(0, _scrollBack + wheel.Delta);
                            break;
                        case InputKey { Key: ConsoleKey.Escape }:
                            canceled = true;
                            break;
                        case InputKey key when ScrollInput.TryKeyScroll(
                            key,
                            scrollPlainArrows: false,
                            pageRows,
                            out var delta):
                            _scrollBack = Math.Max(0, _scrollBack + delta);
                            break;
                        case InputKey { KeyChar: '?' } when buffer.IsEmpty:
                            buffer.Insert("?");
                            break;
                        case InputKey key:
                            if (buffer.Handle(key) == ComposerAction.Submit)
                            {
                                submitted = buffer.Text;
                            }

                            break;
                        default:
                            break;
                    }

                    if (canceled || submitted is not null)
                    {
                        break;
                    }
                }

                _modalOverlay.Clear();
                _overlayWidget = widget(buffer.Text, buffer.Cursor);
                PaintUnlocked(force: true);
            }

            if (canceled)
            {
                return null;
            }

            if (submitted is not null)
            {
                return submitted;
            }
        }
    }

    private void ApplyVerboseToggleUnlocked(DisplayInput.VerboseToggle toggle)
    {
        switch (toggle)
        {
            case DisplayInput.VerboseToggle.Tools:
                _log.VerboseTools = !_log.VerboseTools;
                AddNoteUnlocked($"Verbose tools  {(_log.VerboseTools ? "On" : "Off")}");
                break;
            case DisplayInput.VerboseToggle.Commands:
                _log.VerboseCommands = !_log.VerboseCommands;
                AddNoteUnlocked($"Verbose commands  {(_log.VerboseCommands ? "On" : "Off")}");
                break;
            default:
                return;
        }

        OnVerboseToggled?.Invoke(toggle);
    }

    private void AddNoteUnlocked(string text)
    {
        _log.Add(TranscriptKind.Note, text);
        WriteFallback(TranscriptKind.Note, text);
    }

    private string? DispatchUnlocked(
        IInputEvent item,
        int pageRows,
        Func<bool> togglePlan,
        bool checkSize = true)
    {
        if (checkSize && BelowUsableSize(out _, out _))
        {
            return null;
        }

        if (_fullPageOverlay)
        {
            return DispatchFullPageUnlocked(item, pageRows);
        }

        if (_sideOpen)
        {
            return DispatchSideUnlocked(item);
        }

        switch (item)
        {
            case InputPaste paste:
                _composer.Insert(paste.Text);
                return null;
            case InputWheel wheel:
                _scrollBack = Math.Max(0, _scrollBack + wheel.Delta);
                return null;
            case InputKey key:
                if (DisplayInput.TryToggleVerbose(
                    key,
                    _composer.IsEmpty,
                    _picker is not null,
                    out var verboseToggle))
                {
                    ApplyVerboseToggleUnlocked(verboseToggle);
                    return null;
                }

                if (ScrollInput.TryKeyScroll(
                    key,
                    scrollPlainArrows: false,
                    pageRows,
                    out var delta))
                {
                    _scrollBack = Math.Max(0, _scrollBack + delta);
                    return null;
                }

                return HandleComposerKeyUnlocked(key, togglePlan);
            default:
                return null;
        }
    }

    private string? DispatchFullPageUnlocked(IInputEvent item, int pageRows)
    {
        switch (item)
        {
            case InputWheel wheel:
                _pageScroll = Math.Max(0, _pageScroll - wheel.Delta);
                return null;
            case InputKey key when IsFullPageClose(key):
                CloseFullPageUnlocked();
                return null;
            case InputKey key when ScrollInput.TryKeyScroll(
                key,
                scrollPlainArrows: true,
                pageRows,
                out var delta):
                _pageScroll = Math.Max(0, _pageScroll - delta);
                return null;
            default:
                return null;
        }
    }

    private static bool IsFullPageClose(InputKey key) =>
        key.Modifiers == ConsoleModifiers.None
        && (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Q || key.KeyChar is 'q' or 'Q');

    private void CloseFullPageUnlocked()
    {
        _fullPageOverlay = false;
        _pageScroll = 0;
        _overlayWidget = null;
        _modalOverlay.Clear();
    }

    private string? HandleComposerKeyUnlocked(InputKey key, Func<bool> togglePlan)
    {
        if (_picker is not null
            && key.Key == ConsoleKey.Tab
            && !key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            _composer.Replace(_picker.CompletedText);
            OnComposerEdited?.Invoke(_composer.SubmissionText);
            return null;
        }

        if (_picker is not null
            && key.Key == ConsoleKey.Enter
            && !_picker.IsExact(_composer.Text))
        {
            _composer.Replace(_picker.CompletedText);
            OnComposerEdited?.Invoke(_composer.SubmissionText);
            return null;
        }

        if (_picker is not null && key.Key == ConsoleKey.UpArrow)
        {
            _picker = _picker.Move(-1);
            return null;
        }

        if (_picker is not null && key.Key == ConsoleKey.DownArrow)
        {
            _picker = _picker.Move(1);
            return null;
        }

        var previousText = _composer.Text;
        var action = _composer.Handle(key);
        switch (action)
        {
            case ComposerAction.Submit:
                var text = _composer.SubmissionText;
                _composer.RememberAndClear();
                _picker = null;
                _scrollBack = 0;
                return text;
            case ComposerAction.PasteImage:
                _imagePasteRequested = true;
                break;
            case ComposerAction.TogglePlan:
                _composer.PlanMode = togglePlan();
                _chrome.PlanMode = _composer.PlanMode;
                break;
            case ComposerAction.ShowHelp:
                WriteHelpUnlocked();
                break;
            case ComposerAction.None:
                break;
            default:
                break;
        }

        if (!string.Equals(previousText, _composer.Text, StringComparison.Ordinal)
            && key.Key is not ConsoleKey.UpArrow and not ConsoleKey.DownArrow
            && !(key.Modifiers.HasFlag(ConsoleModifiers.Control)
                && key.Key is ConsoleKey.P or ConsoleKey.N))
        {
            OnComposerEdited?.Invoke(_composer.SubmissionText);
        }

        return null;
    }

    private void WriteHelpUnlocked()
    {
        CommitLiveUnlocked();
        AddHelpUnlocked(
            "/btw         Side question; not saved. Esc or Ctrl+C closes, x clears",
            "enter        Submit; queue while working",
            "empty enter  Send the queue now",
            "queue        Stays above the composer; sends after this tool or turn",
            "ctrl+j       Newline",
            "ctrl+v       Paste clipboard image",
            "\\ enter      Newline",
            "tab          Plan / Work, or complete / and arguments",
            "shift+tab    Plan / Work",
            "?            Shortcuts when empty",
            "ctrl+o       Toggle verbose tool results",
            "ctrl+g       Toggle verbose command output",
            "up/down      Move cursor; at edge, browse prompt history",
            "pageup       Scroll transcript (also pagedown and wheel)",
            "shift+drag   Select terminal text (option or fn on some macOS terminals)");
        foreach (var option in _slashOptions)
        {
            var aliases = option.Keys
                .Where(key => !string.Equals(key, option.Name, StringComparison.OrdinalIgnoreCase))
                .Select(key => "/" + key);
            var names = "/" + option.Name;
            var extra = string.Join("  ", aliases);
            if (extra.Length > 0)
            {
                names += "  " + extra;
            }

            AddHelpUnlocked($"{names,-28}{option.Help}");
        }

        AddHelpUnlocked("ctrl+c      Stop turn; at idle clears input, twice on empty exits");
        PaintUnlocked(force: true);
    }

    private void RefreshPickerUnlocked()
    {
        if (_modalOverlay.Count > 0 || _overlayWidget is not null)
        {
            _picker = null;
            return;
        }

        _picker = SlashPicker.Refresh(_composer.Text, _slashOptions, _picker);
    }

    private void Add(TranscriptKind kind, string text)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            var displayText = ImageMarkerText.Display(text);
            _log.Add(kind, displayText);
            WriteFallback(kind, displayText);
            PaintUnlocked(force: true);
        }
    }

    private void AddHelpUnlocked(params string[] lines)
    {
        foreach (var line in lines)
        {
            _log.Add(TranscriptKind.Note, line);
            WriteFallback(TranscriptKind.Note, line);
        }
    }

    private void OpenLiveUnlocked(TranscriptKind kind)
    {
        if (_streamKind == kind.ToString())
        {
            return;
        }

        CommitLiveUnlocked();
        _streamKind = kind.ToString();
    }

    private void CommitLiveUnlocked()
    {
        if (_streamKind is not null && !Framed && FallbackStreamVisible(_streamKind))
        {
            Console.WriteLine();
        }

        _log.CommitLive();
        _streamKind = null;
        _toolNames.Clear();
    }

    private void DiscardLiveUnlocked()
    {
        _log.DiscardLive();
        _streamKind = null;
        _toolNames.Clear();
    }

    private void PaintUnlocked(bool force)
    {
        if (!Framed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (BelowUsableSize(out var width, out var height))
        {
            var sizeChanged = width != _paintedWidth || height != _paintedHeight;
            if (!force && !sizeChanged && now - _lastPaint < PaintBudget)
            {
                return;
            }

            PaintTooSmallUnlocked(width, height, now);
            return;
        }

        if (!force && now - _lastPaint < PaintBudget)
        {
            return;
        }

        _chrome.TickSpinner(now);
        TickSideSpinner(now);
        RefreshRetryCaptionUnlocked(now);
        if (_fullPageOverlay && _overlayWidget is not null)
        {
            PaintFullPageUnlocked(width, height, now);
            return;
        }

        var composerView = _composer.Project(width, ShellLayout.MaxComposerRows);
        var overlay = OverlayLines(width);
        var queue = QueueLines(width);
        var todos = TodoLines(width);
        var progressWanted = string.IsNullOrWhiteSpace(_chrome.Progress) ? 0 : 1;
        var regions = ShellLayout.Measure(
            width,
            height,
            composerView.Lines.Count,
            overlay.Count,
            queue.Count,
            progressWanted,
            todos.Count);
        if (composerView.Lines.Count > regions.ComposerRows)
        {
            composerView = _composer.Project(width, regions.ComposerRows);
        }

        _scrollBack = _scrollAnchor.Resolve(
            regions.Width,
            _log,
            regions.TranscriptRows,
            _scrollBack);
        _scrollBack = _log.ClampScroll(regions.Width, regions.TranscriptRows, _scrollBack);
        var transcript = _log.Viewport(regions.Width, regions.TranscriptRows, _scrollBack);
        var resetFrame = regions.Width != _paintedWidth || regions.Height != _paintedHeight;
        _painter.Paint(
            regions,
            transcript,
            overlay,
            _chrome.StatusLine(regions.Width),
            queue,
            composerView,
            resetFrame,
            progressWanted == 0 ? null : _chrome.ProgressLine(regions.Width),
            todos,
            showCursor: !_composerPaused && !_sideOpen);
        _paintedWidth = regions.Width;
        _paintedHeight = regions.Height;
        _lastPaint = now;
    }

    private void PaintFullPageUnlocked(int width, int height, DateTimeOffset now)
    {
        var body = _overlayWidget is null
            ? []
            : WidgetPaint.Lines(_overlayWidget, width);
        var frame = new PaintLine[height];
        var start = Math.Clamp(_pageScroll, 0, Math.Max(0, body.Count - 1));
        _pageScroll = start;
        for (var row = 0; row < height; row++)
        {
            var source = start + row;
            frame[row] = source < body.Count ? body[source].Fit(width) : PaintLine.Blank;
        }

        _painter.PaintFrame(
            frame,
            width,
            height,
            resetFrame: width != _paintedWidth || height != _paintedHeight);
        _paintedWidth = width;
        _paintedHeight = height;
        _lastPaint = now;
    }

    private void ApplyUsageUnlocked(
        TokenUsage? usage,
        TokenUsage? cumulativeUsage,
        int contextWindow)
    {
        _lastUsage = usage;
        _cumulativeUsage = cumulativeUsage;
        _chrome.Usage = UsageText.Format(usage, cumulativeUsage, contextWindow);
        _chrome.UsageTotal = UsageText.FormatTotal(cumulativeUsage);
        ApplyCustomUsageUnlocked(usage, cumulativeUsage, contextWindow);
    }

    private void ApplyCustomUsageUnlocked(
        TokenUsage? usage,
        TokenUsage? cumulativeUsage,
        int contextWindow)
    {
        _chrome.ContextUsed = UsageText.FormatContextUsed(usage, contextWindow);
        _chrome.ContextLeft = UsageText.FormatContextLeft(usage, contextWindow);
        _chrome.ContextTokens = UsageText.FormatContextTokens(usage, contextWindow);
        _chrome.RequestInput = UsageText.FormatScoped(usage?.InputTokenCount, "Request In");
        _chrome.RequestOutput = UsageText.FormatScoped(usage?.OutputTokenCount, "Request Out");
        _chrome.RequestTotal = UsageText.FormatScoped(usage?.TotalTokenCount, "Request");
        _chrome.SessionInput = UsageText.FormatScoped(cumulativeUsage?.InputTokenCount, "Session In");
        _chrome.SessionOutput = UsageText.FormatScoped(cumulativeUsage?.OutputTokenCount, "Session Out");
        _chrome.SessionTotal = UsageText.FormatScoped(cumulativeUsage?.TotalTokenCount, "Session");
    }

    private static bool BelowUsableSize(out int width, out int height)
    {
        if (!ScreenSize.TryRead(out width, out height))
        {
            return false;
        }

        return BelowUsableSize(width, height);
    }

    private static bool BelowUsableSize(int width, int height) =>
        width < ShellLayout.MinUsableWidth || height < ShellLayout.MinUsableHeight;

    private void PaintTooSmallUnlocked(int width, int height, DateTimeOffset now)
    {
        var message = $"Terminal too small - resize to at least "
            + $"{ShellLayout.MinUsableWidth}x{ShellLayout.MinUsableHeight} "
            + $"(currently {width}x{height})";
        var paintWidth = Math.Max(1, width);
        var paintHeight = Math.Max(1, height);
        _painter.PaintFrame(
            FrameRows.Notice(paintWidth, paintHeight, message),
            paintWidth,
            paintHeight,
            resetFrame: width != _paintedWidth || height != _paintedHeight);
        _paintedWidth = width;
        _paintedHeight = height;
        _lastPaint = now;
    }

    private string? DispatchSideUnlocked(IInputEvent item)
    {
        switch (item)
        {
            case InputPaste:
                return null;
            case InputWheel wheel:
                ScrollSide(-wheel.Delta);
                return null;
            case InputKey key when IsSideDismiss(key):
                _sideOpen = false;
                return null;
            case InputKey key when IsSideClear(key):
                _sideOpen = false;
                _side = SideQuestionSnapshot.Empty;
                _sideClearRequested = true;
                return null;
            case InputKey key when IsSideCancel(key):
                _sideOpen = false;
                _sideCancelRequested = true;
                return null;
            case InputKey key when key.Modifiers == ConsoleModifiers.None && key.Key == ConsoleKey.LeftArrow:
                MoveSide(-1);
                return null;
            case InputKey key when key.Modifiers == ConsoleModifiers.None && key.Key == ConsoleKey.RightArrow:
                MoveSide(1);
                return null;
            case InputKey key when ScrollInput.TryKeyScroll(
                key,
                scrollPlainArrows: true,
                MaxSideRows,
                out var delta):
                ScrollSide(-delta);
                return null;
            default:
                return null;
        }
    }

    private static bool IsSideDismiss(InputKey key) =>
        key.Modifiers == ConsoleModifiers.None
        && (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar);

    private static bool IsSideClear(InputKey key) =>
        key.Modifiers == ConsoleModifiers.None
        && (key.Key == ConsoleKey.X || key.KeyChar is 'x' or 'X');

    private static bool IsSideCancel(InputKey key) =>
        key.KeyChar == '\u0003'
        || (key.Key == ConsoleKey.C
            && key.Modifiers.HasFlag(ConsoleModifiers.Control)
            && !key.Modifiers.HasFlag(ConsoleModifiers.Alt)
            && !key.Modifiers.HasFlag(ConsoleModifiers.Shift));

    private void MoveSide(int direction)
    {
        if (_side is null)
        {
            return;
        }

        var last = Math.Max(0, SideQuestionWidget.SlotCount(_side) - 1);
        var next = Math.Clamp(_sideIndex + direction, 0, last);
        if (next == _sideIndex)
        {
            return;
        }

        _sideIndex = next;
        _sideScroll = 0;
        _sideStick = false;
    }

    private void ScrollSide(int delta)
    {
        _sideScroll = Math.Max(0, _sideScroll + delta);
        _sideStick = false;
    }

    private IReadOnlyList<PaintLine> SideLines(int width)
    {
        if (_side is null)
        {
            return [];
        }

        var lines = WidgetPaint.Lines(
            SideQuestionWidget.Create(_side, _sideIndex, _sideSpinnerFrame),
            width);
        var max = Math.Max(0, lines.Count - MaxSideRows);
        if (_sideStick)
        {
            _sideScroll = max;
        }
        else
        {
            _sideScroll = Math.Clamp(_sideScroll, 0, max);
            _sideStick = _sideScroll >= max;
        }

        if (_sideScroll == 0 && lines.Count <= MaxSideRows)
        {
            return lines;
        }

        var window = new PaintLine[Math.Min(MaxSideRows, lines.Count - _sideScroll)];
        for (var row = 0; row < window.Length; row++)
        {
            window[row] = lines[_sideScroll + row];
        }

        return window;
    }

    private IReadOnlyList<PaintLine> OverlayLines(int width)
    {
        if (_sideOpen && _side is not null)
        {
            return SideLines(width);
        }

        if (_overlayWidget is not null)
        {
            return WidgetPaint.Lines(_overlayWidget, width);
        }

        if (_modalOverlay.Count > 0)
        {
            var lines = new List<PaintLine>();
            foreach (var line in _modalOverlay)
            {
                lines.Add(PaintLine.Colored(Theme.Review, TextWidth.Truncate("  " + line, width)));
            }

            return lines;
        }

        return _picker is null ? [] : _picker.Paint(width);
    }

    private IReadOnlyList<PaintLine> QueueLines(int width)
    {
        var card = QueueCard.TryCreate(_queueItems);
        return card is null ? [] : WidgetPaint.Lines(card, width);
    }

    private IReadOnlyList<PaintLine> TodoLines(int width) =>
        TodoBar.Lines(_todoItems, width);

    private ShellRegions CurrentRegions()
    {
        _ = ScreenSize.TryRead(out var width, out var height);
        var composerView = _composer.Project(width, ShellLayout.MaxComposerRows);
        var progressWanted = string.IsNullOrWhiteSpace(_chrome.Progress) ? 0 : 1;
        return ShellLayout.Measure(
            width,
            height,
            composerView.Lines.Count,
            OverlayLines(width).Count,
            QueueLines(width).Count,
            progressWanted,
            TodoLines(width).Count);
    }

    private void SetTurnActivityUnlocked(string activity, string progress)
    {
        if (!IsRetryCaption(progress))
        {
            ClearRetryUnlocked();
        }

        _chrome.Activity = activity;
        _chrome.Progress = progress;
        RefreshTokenEstimateUnlocked();
    }

    private void RefreshRetryCaptionUnlocked(DateTimeOffset now)
    {
        if (_retryUntil is not { } deadline)
        {
            return;
        }

        _chrome.ReplaceProgress(ProgressText.Retrying(_retryAttempt, deadline - now));
    }

    private void ClearRetryUnlocked()
    {
        _retryUntil = null;
        _retryAttempt = 0;
    }

    private static bool IsRetryCaption(string progress) =>
        progress.StartsWith("Retrying", StringComparison.Ordinal);

    private bool SideWaiting() =>
        _sideOpen
        && _side is { Running: true } side
        && side.LiveAnswer.Length == 0
        && !string.IsNullOrEmpty(side.PendingQuestion)
        && _sideIndex >= side.Exchanges.Count;

    private bool SideSpinnerDue(DateTimeOffset now) =>
        SideWaiting()
        && (_sideSpinnerAt == default || now - _sideSpinnerAt >= ProgressSpinner.Interval);

    private void TickSideSpinner(DateTimeOffset now)
    {
        if (!SideWaiting())
        {
            _sideSpinnerFrame = 0;
            _sideSpinnerAt = default;
            return;
        }

        if (_sideSpinnerAt == default)
        {
            _sideSpinnerAt = now;
            return;
        }

        if (now - _sideSpinnerAt < ProgressSpinner.Interval)
        {
            return;
        }

        _sideSpinnerFrame++;
        _sideSpinnerAt = now;
    }

    private void RefreshTokenEstimateUnlocked()
    {
        if (!_showEstimatedTokens
            || (_chrome.Progress != ProgressText.Thinking
                && _chrome.Progress != ProgressText.Writing))
        {
            _chrome.TokenEstimate = string.Empty;
            return;
        }

        _chrome.TokenEstimate = UsageText.FormatEstimate(
            TokenEstimator.Characters(_streamedCharacters));
    }

    private bool FallbackStreamVisible(string streamKind) =>
        !string.Equals(streamKind, nameof(TranscriptKind.Thinking), StringComparison.Ordinal)
        || _log.VerboseThinking;

    private bool Framed => _screen is { IsActive: true };

    private void WriteFallback(TranscriptKind kind, string text, string? toolName = null)
    {
        if (Framed)
        {
            return;
        }

        var visible = _log.InlineText(kind, text, toolName);
        if (visible is null)
        {
            return;
        }

        TranscriptFallback.Write(kind, visible);
    }

    private void WriteFallbackDelta(TranscriptKind kind, string text)
    {
        if (!Framed)
        {
            TranscriptFallback.WriteDelta(kind, text);
        }
    }

    private async Task<List<ConsoleKeyInfo>?> ReadAvailableKeysAsync(
        Task? wake,
        bool ignorePause,
        CancellationToken cancellationToken)
    {
        var discardQueued = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paused = false;
            var tooSmall = false;
            lock (_gate)
            {
                paused = !ignorePause && _composerPaused;
                var haveSize = ScreenSize.TryRead(out var pollWidth, out var pollHeight);
                tooSmall = haveSize && BelowUsableSize(pollWidth, pollHeight);
                var sizeChanged = pollWidth != _paintedWidth || pollHeight != _paintedHeight;
                var now = DateTimeOffset.UtcNow;
                if (sizeChanged || _chrome.SpinnerDue(now) || SideSpinnerDue(now))
                {
                    PaintUnlocked(force: true);
                }
            }

            if (tooSmall)
            {
                discardQueued = true;
                await DiscardAvailableKeysAsync(cancellationToken);
                if (wake is { IsCompleted: true })
                {
                    return null;
                }

                await Task.Delay(PollMilliseconds, cancellationToken);
                continue;
            }

            if (discardQueued)
            {
                discardQueued = false;
                await DiscardAvailableKeysAsync(cancellationToken);
            }

            if (!paused && Console.KeyAvailable)
            {
                return await ReadBurstAsync(cancellationToken);
            }

            if (wake is { IsCompleted: true })
            {
                return null;
            }

            await Task.Delay(PollMilliseconds, cancellationToken);
        }
    }

    private async Task DiscardAvailableKeysAsync(CancellationToken cancellationToken)
    {
        if (!Console.KeyAvailable)
        {
            return;
        }

        _ = await ReadBurstAsync(cancellationToken);
        lock (_gate)
        {
            // The keys are ignored. Push would keep an unfinished escape
            // sequence and attach it to the next real key.
            _decoder.Reset();
        }
    }

    private Task<List<ConsoleKeyInfo>> ReadBurstAsync(CancellationToken cancellationToken) =>
        KeyBurst.ReadAsync(
            () => Console.KeyAvailable,
            () => Console.ReadKey(intercept: true),
            token => Task.Delay(EscapeHoldMilliseconds, token),
            cancellationToken);
}
