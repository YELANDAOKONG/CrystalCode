using System.Diagnostics;
using System.Text;

using Spectre.Console;
using Spectre.Console.Rendering;

using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Plugins;
using CrystalHarness.Sessions;

namespace CrystalHarness.Display;

/// <summary>
/// Fullscreen session shell. Alternate buffer, not AnsiConsole.Live.
/// </summary>
public sealed class SessionRenderer : ITurnObserver, ISlashOutput, IDisposable
{
    private const int PollMilliseconds = 40;
    private static readonly TimeSpan PaintBudget = TimeSpan.FromMilliseconds(33);
    private readonly object _gate = new();
    private readonly TranscriptLog _log = new();
    private readonly ComposerBuffer _composer = new();
    private readonly ShellChrome _chrome = new();
    private readonly List<string> _modalOverlay = [];
    private IRenderable? _overlayWidget;
    private readonly List<SlashOption> _slashOptions = [];
    private AlternateScreen? _screen;
    private SlashPicker? _picker;
    private string? _streamKind;
    private string _toolName = string.Empty;
    private Stopwatch? _turnClock;
    private DateTimeOffset _lastPaint;
    private int _scrollBack;
    private bool _composerPaused;

    public IDisposable Open()
    {
        lock (_gate)
        {
            _screen?.Dispose();
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
        }
    }

    public void SetSlashCommands(IReadOnlyList<ISlashCommand>? extras)
    {
        lock (_gate)
        {
            _slashOptions.Clear();
            foreach (var spec in SlashCatalog.BuiltIn)
            {
                var keys = new List<string> { spec.Name };
                keys.AddRange(spec.Aliases);
                _slashOptions.Add(new SlashOption(spec.Name, spec.Help, keys));
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

    public void WriteHeader(
        string model,
        string workspaceRoot,
        bool planMode,
        ApprovalMode approval)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _chrome.Model = model;
            _chrome.WorkspaceRoot = workspaceRoot;
            _chrome.PlanMode = planMode;
            _chrome.Approval = ApprovalLabel.For(approval);
            _composer.PlanMode = planMode;
            if (Framed)
            {
                PaintUnlocked(force: true);
                return;
            }

            var mode = ModeLabel.For(planMode);
            var modeColor = planMode ? Theme.Plan : Theme.Work;
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]{MarkupText.Escape(model)}  ·  [/]"
                + $"[{modeColor}]{mode}[/]"
                + $"[{Theme.Chrome}]  ·  {MarkupText.Escape(ApprovalLabel.For(approval))}  ·  "
                + $"{MarkupText.Escape(PathDisplay.Shorten(workspaceRoot))}[/]");
            AnsiConsole.WriteLine();
        }
    }

    public void SetChrome(bool planMode, ApprovalMode approval)
    {
        lock (_gate)
        {
            _chrome.PlanMode = planMode;
            _composer.PlanMode = planMode;
            _chrome.Approval = ApprovalLabel.For(approval);
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
                "enter        submit; queue while working",
                "enter        empty while working interrupts and sends",
                "ctrl+j       newline",
                "\\ enter      newline",
                "tab          Plan / Work, or complete /",
                "shift+tab    Plan / Work",
                "?            shortcuts when empty",
                "pageup       scroll transcript");
            foreach (var spec in SlashCatalog.BuiltIn)
            {
                var names = "/" + spec.Name;
                if (spec.Aliases.Count > 0)
                {
                    names += "  " + string.Join("  ", spec.Aliases.Select(alias => "/" + alias));
                }

                AddHelpUnlocked($"{names,-28}{spec.Help}");
            }

            AddHelpUnlocked("ctrl+c      stop turn; twice at idle exits");
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

    public void WriteStatus(
        SessionLedger ledger,
        string workspaceRoot,
        bool planMode,
        ApprovalMode approval,
        int contextWindow)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _chrome.WorkspaceRoot = workspaceRoot;
            _chrome.PlanMode = planMode;
            _chrome.Approval = ApprovalLabel.For(approval);
            _chrome.Usage = UsageText.Format(ledger.Usage, contextWindow);
            var text = $"{ModeLabel.For(planMode)}  ·  {ApprovalLabel.For(approval)}  ·  "
                + $"{ledger.UserTurns} turns  ·  {ledger.ModelCalls} model  ·  "
                + $"{ledger.ToolCalls} tools  ·  {_chrome.Usage}";
            _log.Add(TranscriptKind.Note, text);
            WriteFallback(TranscriptKind.Note, text);
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
            _chrome.Usage = UsageText.Format(result.Usage, contextWindow);
            _chrome.ToolCount = result.ToolCallCount;
            _chrome.Elapsed = _turnClock is null
                ? string.Empty
                : UsageText.FormatElapsed(_turnClock.Elapsed);
            _chrome.Activity = string.Empty;
            if (result.StopReason != TurnStopReason.Completed)
            {
                _log.Add(TranscriptKind.Note, result.StopReason.Value);
                WriteFallback(TranscriptKind.Note, result.StopReason.Value);
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

    public void BeginTurn()
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            _turnClock = Stopwatch.StartNew();
            _toolName = string.Empty;
            _chrome.Activity = "Running";
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
                    _chrome.Activity = "Thinking";
                    WriteFallbackDelta(TranscriptKind.Thinking, reasoning.Text);
                    PaintUnlocked(force: false);
                    break;
                case ChatTextDelta text when text.Text.Length > 0:
                    OpenLiveUnlocked(TranscriptKind.Assistant);
                    _log.AppendLive(TranscriptKind.Assistant, text.Text);
                    _chrome.Activity = "Writing";
                    WriteFallbackDelta(TranscriptKind.Assistant, text.Text);
                    PaintUnlocked(force: false);
                    break;
                case ChatToolCallDelta toolCall:
                    if (toolCall.NameDelta.Length > 0)
                    {
                        _toolName += toolCall.NameDelta;
                        _chrome.Activity = _toolName;
                    }

                    PaintUnlocked(force: false);
                    break;
                default:
                    break;
            }
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

    public void OnToolResults(IReadOnlyList<ToolResult> results)
    {
        lock (_gate)
        {
            CommitLiveUnlocked();
            foreach (var result in results)
            {
                var first = ToolResultText.Summary(result.Text);
                var kind = result.Status == ToolResultStatus.Success
                    ? TranscriptKind.Result
                    : TranscriptKind.Error;
                _log.Add(kind, first);
                WriteFallback(kind, first);
            }

            _chrome.Activity = "Running";
            PaintUnlocked(force: true);
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

    public void SetQueued(int count)
    {
        lock (_gate)
        {
            _chrome.Queued = Math.Max(0, count);
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
                if (burst.Count > 1)
                {
                    _composer.Insert(ExtractPaste(burst));
                }
                else
                {
                    submitted = HandleComposerKeyUnlocked(burst[0], togglePlan);
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

    public async Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        while (!Console.KeyAvailable)
        {
            await Task.Delay(PollMilliseconds, cancellationToken);
        }

        return Console.ReadKey(intercept: true);
    }

    private string? HandleComposerKeyUnlocked(ConsoleKeyInfo key, Func<bool> togglePlan)
    {
        if (key.Key == ConsoleKey.PageUp)
        {
            var regions = CurrentRegions();
            _scrollBack += Math.Max(1, regions.TranscriptRows - 1);
            return null;
        }

        if (key.Key == ConsoleKey.PageDown)
        {
            var regions = CurrentRegions();
            _scrollBack = Math.Max(0, _scrollBack - Math.Max(1, regions.TranscriptRows - 1));
            return null;
        }

        if (_picker is not null
            && key.Key == ConsoleKey.Tab
            && !key.Modifiers.HasFlag(ConsoleModifiers.Shift))
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
            "enter        submit; queue while working",
            "enter        empty while working interrupts and sends",
            "ctrl+j       newline",
            "\\ enter      newline",
            "tab          Plan / Work, or complete /",
            "shift+tab    Plan / Work",
            "?            shortcuts when empty",
            "pageup       scroll transcript");
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

        AddHelpUnlocked("ctrl+c      stop turn; twice at idle exits");
        PaintUnlocked(force: true);
    }

    private void RefreshPickerUnlocked()
    {
        if (_modalOverlay.Count > 0 || _overlayWidget is not null)
        {
            _picker = null;
            return;
        }

        _picker = SlashPicker.Create(_composer.Text, _slashOptions);
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
        _toolName = string.Empty;
    }

    private void PaintUnlocked(bool force)
    {
        if (!Framed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastPaint < PaintBudget)
        {
            return;
        }

        var composerView = _composer.Project(ScreenSize.Width, ShellLayout.MaxComposerRows);
        var overlay = OverlayLines(ScreenSize.Width);
        var regions = ShellLayout.Measure(
            ScreenSize.Width,
            ScreenSize.Height,
            composerView.Lines.Count,
            overlay.Count);
        _scrollBack = _log.ClampScroll(regions.Width, regions.TranscriptRows, _scrollBack);
        var transcript = _log.Viewport(regions.Width, regions.TranscriptRows, _scrollBack);
        ScreenPainter.Paint(
            regions,
            transcript,
            overlay,
            _chrome.StatusLine(regions.Width),
            composerView);
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

    private ShellRegions CurrentRegions()
    {
        var composerView = _composer.Project(ScreenSize.Width, ShellLayout.MaxComposerRows);
        return ShellLayout.Measure(
            ScreenSize.Width,
            ScreenSize.Height,
            composerView.Lines.Count,
            OverlayLines(ScreenSize.Width).Count);
    }

    private bool Framed => _screen is { IsActive: true };

    private void WriteFallback(TranscriptKind kind, string text)
    {
        if (Framed)
        {
            return;
        }

        var color = kind switch
        {
            TranscriptKind.Error => Theme.Fail,
            TranscriptKind.Result => Theme.Ok,
            TranscriptKind.Thinking => Theme.Thinking,
            TranscriptKind.Tool => Theme.Tool,
            TranscriptKind.Approval => Theme.Review,
            TranscriptKind.User => Theme.User,
            _ => Theme.Chrome
        };
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            AnsiConsole.MarkupLine($"[{color}]  {MarkupText.Escape(line)}[/]");
        }
    }

    private void WriteFallbackDelta(TranscriptKind kind, string text)
    {
        if (Framed)
        {
            return;
        }

        var color = kind switch
        {
            TranscriptKind.Thinking => Theme.Thinking,
            TranscriptKind.Tool => Theme.Tool,
            _ => Theme.User
        };
        if (kind is TranscriptKind.Thinking or TranscriptKind.Tool)
        {
            AnsiConsole.Markup($"[{color}]{MarkupText.Escape(text)}[/]");
            return;
        }

        AnsiConsole.Markup(MarkupText.Escape(text));
    }

    private async Task<List<ConsoleKeyInfo>?> ReadAvailableKeysAsync(
        Task? wake,
        bool ignorePause,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paused = false;
            lock (_gate)
            {
                paused = !ignorePause && _composerPaused;
            }

            if (!paused && Console.KeyAvailable)
            {
                var burst = new List<ConsoleKeyInfo> { Console.ReadKey(intercept: true) };
                while (Console.KeyAvailable)
                {
                    burst.Add(Console.ReadKey(intercept: true));
                }

                return burst;
            }

            if (wake is { IsCompleted: true })
            {
                return null;
            }

            await Task.Delay(PollMilliseconds, cancellationToken);
        }
    }

    private static string ExtractPaste(List<ConsoleKeyInfo> burst)
    {
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                text.Append('\n');
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        return text.ToString();
    }
}
