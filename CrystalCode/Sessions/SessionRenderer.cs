using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Rendering;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Approvals;
using CrystalCode.Compaction;
using CrystalCode.Plugins.Interfaces;
using CrystalCode.Prompts;
using CrystalCode.Display.Cards;
using CrystalCode.Display.Composer;
using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Display.Transcript;

namespace CrystalCode.Sessions;

/// <summary>
/// Fullscreen session shell. Alternate buffer, not AnsiConsole.Live.
/// </summary>
public sealed class SessionRenderer : ITurnObserver, ISlashOutput, IDisposable
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
    private readonly InputDecoder _decoder = new();
    private AlternateScreen? _screen;
    private SlashPicker? _picker;
    private string? _streamKind;
    private readonly StreamToolNames _toolNames = new();
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
    private TokenUsage? _turnCumulativeBaseline;
    private DateTimeOffset? _retryUntil;
    private int _retryAttempt;

    public int ContextWindow { get; set; }

    public Action? AfterTools { get; set; }

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

    public void SetSlashCommands(
        IReadOnlyList<ISlashCommand>? extras,
        IReadOnlyList<SlashOption>? thinkingArguments = null,
        IReadOnlyList<SlashOption>? modelArguments = null,
        IReadOnlyList<SlashOption>? promptSetArguments = null,
        IReadOnlyList<SlashOption>? toolArguments = null)
    {
        lock (_gate)
        {
            _slashOptions.Clear();
            _picker = null;
            foreach (var spec in SlashCatalog.BuiltIn)
            {
                var keys = new List<string> { spec.Name };
                keys.AddRange(spec.Aliases);
                var arguments = ArgumentsFor(
                    spec,
                    thinkingArguments,
                    modelArguments,
                    promptSetArguments,
                    toolArguments);
                _slashOptions.Add(new SlashOption(spec.Name, spec.Help, keys, arguments));
            }

            if (extras is null)
            {
                return;
            }

            foreach (var command in extras)
            {
                _slashOptions.Add(new SlashOption(command.Name, command.Help, [command.Name]));
            }
        }
    }

    private static IReadOnlyList<SlashOption> ArgumentsFor(
        SlashSpec spec,
        IReadOnlyList<SlashOption>? thinkingArguments,
        IReadOnlyList<SlashOption>? modelArguments,
        IReadOnlyList<SlashOption>? promptSetArguments,
        IReadOnlyList<SlashOption>? toolArguments)
    {
        if (spec.Verb == SessionVerb.Thinking && thinkingArguments is not null)
        {
            return thinkingArguments;
        }

        if (spec.Verb == SessionVerb.Model && modelArguments is not null)
        {
            return modelArguments;
        }

        if (spec.Verb == SessionVerb.PromptSet && promptSetArguments is not null)
        {
            return promptSetArguments;
        }

        if (spec.Verb == SessionVerb.Tools && toolArguments is not null)
        {
            return toolArguments;
        }

        return ToArgumentOptions(spec.Arguments);
    }

    private static IReadOnlyList<SlashOption> ToArgumentOptions(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return [];
        }

        var options = new List<SlashOption>(arguments.Count);
        foreach (var argument in arguments)
        {
            options.Add(new SlashOption(argument, argument, [argument]));
        }

        return options;
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
            if (!Framed)
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
                "enter        Empty while working interrupts and sends",
                "queue        Stays above the composer; sends after this tool or turn",
                "ctrl+j       Newline",
                "\\ enter      Newline",
                "tab          Plan / Work, or complete / and arguments",
                "shift+tab    Plan / Work",
                "?            Shortcuts when empty",
                "pageup       Scroll transcript (also wheel, ctrl+up/down, empty up)",
                "up/down      history recall (or picker navigation)");
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

    public void WriteTurnFooter(
        TurnResult result,
        SessionLedger ledger,
        int contextWindow)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            ApplyUsageUnlocked(
                ledger.Usage ?? result.Usage,
                ledger.CumulativeUsage,
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
            PaintUnlocked(force: true);
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
            foreach (var line in TranscriptReplay.Lines(items))
            {
                _log.Add(line.Kind, line.Text);
                WriteFallback(line.Kind, line.Text);
            }

            PaintUnlocked(force: true);
        }
    }

    public void BeginTurn()
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _turnCumulativeBaseline = _cumulativeUsage;
            _turnClock = Stopwatch.StartNew();
            _toolNames.Clear();
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
                    _log.AppendLive(TranscriptKind.Thinking, reasoning.Text);
                    _streamedCharacters += reasoning.Text.Length;
                    SetTurnActivityUnlocked("Thinking", ProgressText.Thinking);
                    WriteFallbackDelta(TranscriptKind.Thinking, reasoning.Text);
                    PaintUnlocked(force: false);
                    break;
                case ChatTextDelta text when text.Text.Length > 0:
                    OpenLiveUnlocked(TranscriptKind.Assistant);
                    _log.AppendLive(TranscriptKind.Assistant, text.Text);
                    _streamedCharacters += text.Text.Length;
                    SetTurnActivityUnlocked("Writing", ProgressText.Writing);
                    WriteFallbackDelta(TranscriptKind.Assistant, text.Text);
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
                _log.Add(TranscriptKind.Tool, text);
                WriteFallback(TranscriptKind.Tool, text);
            }

            if (calls.Count > 0)
            {
                SetTurnActivityUnlocked(
                    DisplayCase.Token(calls[^1].Name),
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
                _log.Add(kind, body);
                WriteFallback(kind, body);
            }

            _chrome.ToolCount += results.Count;
            _streamedCharacters = 0;
            SetTurnActivityUnlocked("Running", ProgressText.WaitingForModel);
            PaintUnlocked(force: true);
        }

        AfterTools?.Invoke();
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

    internal void ShowContextUsage(TokenUsage? usage)
    {
        lock (_gate)
        {
            ApplyContextUsageUnlocked(usage, ContextWindow);
            PaintUnlocked(force: true);
        }
    }

    public void OnUsageUpdated(TokenUsage? contextUsage, TokenUsage? turnCumulativeUsage = null)
    {
        lock (_gate)
        {
            TokenUsage? cumulative = _cumulativeUsage;
            if (turnCumulativeUsage is not null)
            {
                cumulative = SessionLedger.Add(_turnCumulativeBaseline, turnCumulativeUsage);
            }

            if (contextUsage is not null || turnCumulativeUsage is not null)
            {
                ApplyUsageUnlocked(contextUsage ?? _lastUsage, cumulative, ContextWindow);
            }

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
            _modalOverlay.Clear();
            _overlayWidget = widget;
            PaintUnlocked(force: true);
        }
    }

    public void ClearOverlay()
    {
        lock (_gate)
        {
            _modalOverlay.Clear();
            _overlayWidget = null;
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

            string? submitted = null;
            lock (_gate)
            {
                var pageRows = Math.Max(1, CurrentRegions().TranscriptRows - 1);
                foreach (var item in _decoder.Push(burst))
                {
                    submitted = DispatchUnlocked(item, pageRows, togglePlan);
                    if (submitted is not null)
                    {
                        break;
                    }
                }

                RefreshPickerUnlocked();
                PaintUnlocked(force: true);
            }

            if (submitted is not null)
            {
                var text = submitted.Trim();
                if (text.Length > 0)
                {
                    onSubmit?.Invoke(text);
                }
            }
        }

        await wake;
    }

    public void PauseComposer()
    {
        lock (_gate)
        {
            _composerPaused = true;
        }
    }

    public void ResumeComposer()
    {
        lock (_gate)
        {
            _composerPaused = false;
            PaintUnlocked(force: true);
        }
    }

    public void SetQueue(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            _queueItems.Clear();
            _queueItems.AddRange(items);
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
            if (_composer.IsEmpty)
            {
                return false;
            }

            _composer.Clear();
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

            string? submitted = null;
            lock (_gate)
            {
                var pageRows = Math.Max(1, CurrentRegions().TranscriptRows - 1);
                foreach (var item in _decoder.Push(burst))
                {
                    submitted = DispatchUnlocked(item, pageRows, togglePlan);
                    if (submitted is not null)
                    {
                        break;
                    }
                }

                RefreshPickerUnlocked();
                PaintUnlocked(force: true);
            }

            if (submitted is not null)
            {
                return PromptRead.Submitted(submitted);
            }
        }
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
                foreach (var item in _decoder.Push(burst))
                {
                    switch (item)
                    {
                        case InputPaste:
                            PaintUnlocked(force: true);
                            break;
                        case InputWheel wheel:
                            _scrollBack = Math.Max(0, _scrollBack + wheel.Delta);
                            PaintUnlocked(force: true);
                            break;
                        case InputKey key:
                            if (TryReadKeyScroll(
                                key,
                                scrollPlainArrows,
                                pageRows,
                                out var delta))
                            {
                                _scrollBack = Math.Max(0, _scrollBack + delta);
                                PaintUnlocked(force: true);
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
            composerEmpty: scrollPlainArrows,
            pickerOpen: false,
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
                            composerEmpty: false,
                            pickerOpen: false,
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

    private string? DispatchUnlocked(IInputEvent item, int pageRows, Func<bool> togglePlan)
    {
        if (BelowUsableSize(out _, out _))
        {
            return null;
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
                if (ScrollInput.TryKeyScroll(
                    key,
                    _composer.IsEmpty,
                    _picker is not null,
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

    private string? HandleComposerKeyUnlocked(InputKey key, Func<bool> togglePlan)
    {
        if (_picker is not null
            && key.Key == ConsoleKey.Tab
            && !key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            _composer.Replace(_picker.CompletedText);
            return null;
        }

        if (_picker is not null
            && key.Key == ConsoleKey.Enter
            && !_picker.IsExact(_composer.Text))
        {
            _composer.Replace(_picker.CompletedText);
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

        var action = _composer.Handle(key);
        switch (action)
        {
            case ComposerAction.Submit:
                var text = _composer.Text;
                _composer.RememberAndClear();
                _picker = null;
                _scrollBack = 0;
                return text;
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

        return null;
    }

    private void WriteHelpUnlocked()
    {
        CommitLiveUnlocked();
        AddHelpUnlocked(
            "enter        Submit; queue while working",
            "enter        Empty while working interrupts and sends",
            "queue        Stays above the composer; sends after this tool or turn",
            "ctrl+j       Newline",
            "\\ enter      Newline",
            "tab          Plan / Work, or complete / and arguments",
            "shift+tab    Plan / Work",
            "?            Shortcuts when empty",
            "pageup       Scroll transcript (also wheel, ctrl+up/down, empty up)");
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
            _log.Add(kind, text);
            WriteFallback(kind, text);
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
        if (_streamKind is not null && !Framed)
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
        RefreshRetryCaptionUnlocked(now);

        var composerView = _composer.Project(ScreenSize.Width, ShellLayout.MaxComposerRows);
        var overlay = OverlayLines(ScreenSize.Width);
        var queue = QueueLines(ScreenSize.Width);
        var todos = TodoLines(ScreenSize.Width);
        var progressWanted = string.IsNullOrWhiteSpace(_chrome.Progress) ? 0 : 1;
        var regions = ShellLayout.Measure(
            ScreenSize.Width,
            ScreenSize.Height,
            composerView.Lines.Count,
            overlay.Count,
            queue.Count,
            progressWanted,
            todos.Count);
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
            showCursor: !_composerPaused);
        _paintedWidth = regions.Width;
        _paintedHeight = regions.Height;
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
    }

    private void ApplyContextUsageUnlocked(TokenUsage? usage, int contextWindow)
    {
        _lastUsage = usage;
        _chrome.Usage = UsageText.Format(usage, _cumulativeUsage, contextWindow);
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
            paintHeight,
            resetFrame: width != _paintedWidth || height != _paintedHeight);
        _paintedWidth = width;
        _paintedHeight = height;
        _lastPaint = now;
    }

    private IReadOnlyList<PaintLine> OverlayLines(int width)
    {
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
        var composerView = _composer.Project(ScreenSize.Width, ShellLayout.MaxComposerRows);
        var progressWanted = string.IsNullOrWhiteSpace(_chrome.Progress) ? 0 : 1;
        return ShellLayout.Measure(
            ScreenSize.Width,
            ScreenSize.Height,
            composerView.Lines.Count,
            OverlayLines(ScreenSize.Width).Count,
            QueueLines(ScreenSize.Width).Count,
            progressWanted,
            TodoLines(ScreenSize.Width).Count);
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

    private bool Framed => _screen is { IsActive: true };

    private void WriteFallback(TranscriptKind kind, string text)
    {
        if (!Framed)
        {
            TranscriptFallback.Write(kind, text);
        }
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
                var sizeChanged = haveSize
                    ? pollWidth != _paintedWidth || pollHeight != _paintedHeight
                    : ScreenSize.Width != _paintedWidth || ScreenSize.Height != _paintedHeight;
                if (sizeChanged || _chrome.SpinnerDue(DateTimeOffset.UtcNow))
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

        var burst = await ReadBurstAsync(cancellationToken);
        lock (_gate)
        {
            _decoder.Push(burst);
        }
    }

    private Task<List<ConsoleKeyInfo>> ReadBurstAsync(CancellationToken cancellationToken) =>
        KeyBurst.ReadAsync(
            () => Console.KeyAvailable,
            () => Console.ReadKey(intercept: true),
            token => Task.Delay(EscapeHoldMilliseconds, token),
            cancellationToken);
}
