using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Persistent chrome fields for one session: status bar and progress row.
/// </summary>
public sealed class ShellChrome
{
    private static readonly string[] DropSlots = ["total", "elapsed", "prompt", "model", "path"];
    private const string PendingSlot = "pending";
    private int _spinnerFrame;
    private DateTimeOffset _lastSpinner;
    private string _progress = string.Empty;
    private string _activity = string.Empty;
    private DateTimeOffset _progressStarted;
    private string _elapsedLabel = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public bool PlanMode { get; set; }

    public string? CustomMode { get; set; }

    public string Approval { get; set; } = string.Empty;

    /// <summary>
    /// When off, the approval mode uses the chrome color whatever the theme sets.
    /// </summary>
    public bool ApprovalColors { get; set; } = true;

    public string Thinking { get; set; } = string.Empty;

    public string ServiceTier { get; set; } = string.Empty;

    public string Speed { get; set; } = string.Empty;

    public string ContextWindow { get; set; } = string.Empty;

    public string PromptSet { get; set; } = string.Empty;

    public string Usage { get; set; } = "CTX --";

    public string UsageTotal { get; set; } = string.Empty;

    public bool CustomStatusLine { get; set; }

    public IReadOnlyList<string> StatusLineFields { get; set; } = [];

    public IReadOnlyList<StatusFieldRule> StatusLineRules { get; set; } = [];

    /// <summary>Custom fields to hide first, in this order, when the line does not fit.</summary>
    public IReadOnlyList<string> StatusLineDropOrder { get; set; } = [];

    public IReadOnlyDictionary<string, string> PluginFields { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Text of the <c>approval-*</c> and <c>extended-*</c> custom fields. A
    /// reviewer that is off has no entries, so its fields occupy no cell.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReviewerFields { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string ContextUsed { get; set; } = "CTX --";

    public string ContextLeft { get; set; } = "CTX -- Left";

    public string ContextTokens { get; set; } = "-- CTX";

    public string RequestInput { get; set; } = string.Empty;

    public string RequestOutput { get; set; } = string.Empty;

    public string RequestTotal { get; set; } = string.Empty;

    public string SessionInput { get; set; } = string.Empty;

    public string SessionOutput { get; set; } = string.Empty;

    public string SessionTotal { get; set; } = string.Empty;

    /// <summary>
    /// Status-bar activity. Tool names come from the model, so the text is
    /// reduced to one clean line.
    /// </summary>
    public string Activity
    {
        get => _activity;
        set => _activity = TerminalText.SanitizeLine(value ?? string.Empty);
    }

    public string Progress
    {
        get => _progress;
        set
        {
            var next = TerminalText.SanitizeLine(value ?? string.Empty);
            if (string.Equals(_progress, next, StringComparison.Ordinal))
            {
                return;
            }

            _progress = next;
            _progressStarted = default;
            _elapsedLabel = string.IsNullOrWhiteSpace(next)
                ? string.Empty
                : ProgressElapsed.Format(TimeSpan.Zero);
        }
    }

    public void ReplaceProgress(string progress)
    {
        _progress = TerminalText.SanitizeLine(progress ?? string.Empty);
    }

    public string TokenEstimate { get; set; } = string.Empty;

    public int ToolCount { get; set; }

    public string Elapsed { get; set; } = string.Empty;

    public int Queued { get; set; }

    /// <summary>
    /// Settings chosen during a turn that apply on the next turn, as one
    /// label such as <c>Next: Plan, Approval Full</c>. Empty when none wait.
    /// The active values stay in their own fields.
    /// </summary>
    public string Pending { get; set; } = string.Empty;

    private string ModeLabel =>
        string.IsNullOrEmpty(CustomMode) ? PlanMode ? "Plan" : "Work" : CustomMode;

    private string ModeColor =>
        !string.IsNullOrEmpty(CustomMode) ? Theme.Custom : PlanMode ? Theme.Plan : Theme.Work;

    /// <summary>The first 8 characters of the session id; the full id is in <c>/status</c>.</summary>
    private string ShortSessionId => SessionId.Length <= 8 ? SessionId : SessionId[..8];

    private string ApprovalColor => ApprovalColors ? Theme.ForApproval(Approval) : Theme.Chrome;

    public PaintLine StatusLine(int width)
    {
        var items = new List<(string Plain, string Markup, string Slot)>();

        if (CustomStatusLine)
        {
            AddCustomItems(items, width);
            AddPending(items);
            return FitStatusLine(items, width, IndexOfCustomDroppable);
        }

        if (!string.IsNullOrWhiteSpace(Approval))
        {
            items.Add((Approval, $"[{ApprovalColor}]{MarkupText.Escape(Approval)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Thinking))
        {
            items.Add((Thinking, $"[{ValueColor("thinking", Thinking)}]{MarkupText.Escape(Thinking)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(ServiceTier))
        {
            items.Add((ServiceTier, $"[{ValueColor("tier", ServiceTier)}]{MarkupText.Escape(ServiceTier)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Speed))
        {
            items.Add((Speed, $"[{ValueColor("speed", Speed)}]{MarkupText.Escape(Speed)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(ContextWindow))
        {
            items.Add((ContextWindow, $"[{WindowColor(ContextWindow)}]{MarkupText.Escape(ContextWindow)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(PromptSet))
        {
            var prompt = "Prompt " + PromptSet;
            items.Add((prompt, $"[{Theme.ForValue("prompt-set", PromptSet)}]{MarkupText.Escape(prompt)}[/]", "prompt"));
        }

        if (!string.IsNullOrWhiteSpace(Activity))
        {
            var activityPlain = "• " + Activity;
            items.Add((activityPlain, $"[{Theme.Accent}]{MarkupText.Escape(activityPlain)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Model))
        {
            items.Add((Model, $"[{Theme.ForValue("model", Model, Theme.User)}]{MarkupText.Escape(Model)}[/]", "model"));
        }

        if (!string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            var shortPath = PathDisplay.Shorten(WorkspaceRoot);
            items.Add((shortPath, $"[{Theme.ForStatus("path", Theme.Muted)}]{MarkupText.Escape(shortPath)}[/]", "path"));
        }

        var usageColor = ContextColor(Usage, left: false);
        items.Add((Usage, UsageMarkup(usageColor), "keep"));
        if (!string.IsNullOrWhiteSpace(UsageTotal))
        {
            items.Add((UsageTotal, $"[{Theme.ForStatus("total", usageColor)}]{MarkupText.Escape(UsageTotal)}[/]", "total"));
        }

        if (ToolCount > 0)
        {
            var toolStr = ToolCount == 1 ? "1 Tool" : $"{ToolCount} Tools";
            items.Add((toolStr, $"[{Theme.ForStatus("tools", Theme.Chrome)}]{toolStr}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Elapsed))
        {
            items.Add((Elapsed, $"[{Theme.ForStatus("elapsed", Theme.Chrome)}]{MarkupText.Escape(Elapsed)}[/]", "elapsed"));
        }

        if (Queued > 0)
        {
            var queueStr = $"Queued {Queued}";
            items.Add((queueStr, $"[{Theme.Warning} bold]{queueStr}[/]", "keep"));
        }

        AddPending(items);
        return FitStatusLine(items, width, IndexOfDroppable);
    }

    // Shown on every status line, custom or not, so the operator always sees
    // that a choice made during the turn has not applied yet.
    private void AddPending(List<(string Plain, string Markup, string Slot)> items)
    {
        if (!string.IsNullOrWhiteSpace(Pending))
        {
            items.Add((Pending, $"[{Theme.Warning}]{MarkupText.Escape(Pending)}[/]", PendingSlot));
        }
    }

    private void AddCustomItems(List<(string Plain, string Markup, string Slot)> items, int width)
    {
        foreach (var field in StatusLineFields)
        {
            if (RuleFor(field) is { HideBelow: { } hideBelow } && width < hideBelow)
            {
                continue;
            }

            var value = field switch
            {
                "approval" => Approval,
                "thinking" => Thinking,
                "tier" => ServiceTier,
                "speed" => Speed,
                "context-window" => ContextWindow,
                "prompt-set" => string.IsNullOrWhiteSpace(PromptSet)
                    ? string.Empty
                    : "Prompt " + PromptSet,
                "activity" => string.IsNullOrWhiteSpace(Activity) ? string.Empty : "• " + Activity,
                "model" => Model,
                "provider" => Provider,
                "mode" => ModeLabel,
                "session-id" => ShortSessionId,
                "workspace" => string.IsNullOrWhiteSpace(WorkspaceRoot)
                    ? string.Empty
                    : PathDisplay.Shorten(WorkspaceRoot),
                "context-used" => ContextUsed,
                "context-left" => ContextLeft,
                "context-tokens" => ContextTokens,
                "request-input" => RequestInput,
                "request-output" => RequestOutput,
                "request-total" => RequestTotal,
                "session-input" => SessionInput,
                "session-output" => SessionOutput,
                "session-total" => SessionTotal,
                "tools" => ToolCount switch { 0 => string.Empty, 1 => "1 Tool", _ => $"{ToolCount} Tools" },
                "elapsed" => Elapsed,
                "queued" => Queued > 0 ? $"Queued {Queued}" : string.Empty,
                _ => ReviewerFields.TryGetValue(field, out var reviewer) ? reviewer : PluginField(field)
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                var color = field switch
                {
                    "approval" => ApprovalColor,
                    "context-used" => ContextColor(value, left: false),
                    "context-left" => ContextColor(value, left: true),
                    "thinking" => ValueColor("thinking", value),
                    "tier" => ValueColor("tier", value),
                    "speed" => ValueColor("speed", value),
                    "context-window" => WindowColor(value),
                    "prompt-set" => ValueColor("prompt-set", value),
                    "model" => Theme.ForValue("model", value),
                    "provider" => Theme.ForValue("provider", value),
                    "mode" => Theme.ForValue("mode", value, ModeColor),
                    "request-input" or "session-input" => Theme.ForStatus("input", Theme.Chrome),
                    "request-output" or "session-output" => Theme.ForStatus("output", Theme.Chrome),
                    "request-total" or "session-total" => Theme.ForStatus("total", Theme.Chrome),
                    "workspace" => Theme.ForStatus("path", Theme.Chrome),
                    "tools" => Theme.ForStatus("tools", Theme.Chrome),
                    "elapsed" => Theme.ForStatus("elapsed", Theme.Chrome),
                    _ => ReviewerColor(field, value)
                };
                items.Add((value, $"[{color}]{MarkupText.Escape(value)}[/]", field));
            }
        }
    }

    /// <summary>
    /// Markup for the usage text <c>CTX 6.6%  ·  1.1M IN / 7917 OUT</c>. It is one color unless
    /// the theme colors <c>input</c> or <c>output</c>, which then paint their own parts.
    /// </summary>
    private string UsageMarkup(string contextColor)
    {
        const string Separator = "  ·  ";
        const string Divider = " / ";
        var split = Usage.IndexOf(Separator, StringComparison.Ordinal);
        var tail = split < 0 ? string.Empty : Usage[(split + Separator.Length)..];
        var divide = tail.IndexOf(Divider, StringComparison.Ordinal);
        if ((!Theme.HasStatusColor("input") && !Theme.HasStatusColor("output")) || divide < 0)
        {
            return $"[{contextColor}]{MarkupText.Escape(Usage)}[/]";
        }

        return $"[{contextColor}]{MarkupText.Escape(Usage[..split])}{Separator}[/]"
            + $"[{Theme.ForStatus("input", contextColor)}]{MarkupText.Escape(tail[..divide])}[/]"
            + $"[{contextColor}]{Divider}[/]"
            + $"[{Theme.ForStatus("output", contextColor)}]{MarkupText.Escape(tail[(divide + Divider.Length)..])}[/]";
    }

    /// <summary>
    /// Style for a context text such as <c>CTX ~7.6%</c> or <c>CTX 92.4% Left</c>.
    /// A "left" text is turned into used percent first, so one theme range
    /// colors both. Text without a percentage uses chrome.
    /// </summary>
    private static string ContextColor(string text, bool left)
    {
        var percentIndex = text.IndexOf('%', StringComparison.Ordinal);
        if (percentIndex < 0)
        {
            return Theme.Chrome;
        }

        var start = text.LastIndexOf(' ', percentIndex) + 1;
        if (!double.TryParse(
                text[start..percentIndex].TrimStart('~'),
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var percent))
        {
            return Theme.Chrome;
        }

        return Theme.ForContext(left ? 100 - percent : percent);
    }

    // A reviewer field reads "Reviewer <value>", "Reviewer Think <value>", or
    // "Extended Trigger <value>", so the theme rule sees the value alone. A
    // field's own gauge wins; without one it follows the main status line's.
    private static string ReviewerColor(string field, string text)
    {
        var (shared, words) = field switch
        {
            "approval-provider" or "extended-provider" => ("provider", 1),
            "approval-model" or "extended-model" => ("model", 1),
            "approval-thinking" or "extended-thinking" => ("thinking", 2),
            "approval-tier" or "extended-tier" => ("tier", 2),
            "approval-speed" or "extended-speed" => ("speed", 2),
            "extended-trigger" or "extended-primary" => (null, 2),
            _ => (null, 0)
        };
        if (words == 0)
        {
            return Theme.Chrome;
        }

        var parts = text.Split(' ', words + 1);
        return Theme.ForReviewerValue(field, shared, parts.Length > words ? parts[words] : text);
    }

    /// <summary>
    /// Style for a status text such as <c>Think High</c>, <c>Tier Priority</c>,
    /// <c>Speed Fast</c>, <c>Context 200K</c>, or <c>Prompt Review</c>. A theme
    /// rule sees the value alone, so the label word is removed first.
    /// </summary>
    private static string ValueColor(string gauge, string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return Theme.ForValue(gauge, space < 0 ? text : text[(space + 1)..]);
    }

    /// <summary>
    /// Style for <c>Context 200K</c>. A from/to rule sees the window in tokens
    /// (<c>200K</c> is 200000, <c>1.5M</c> is 1500000); a match or regex rule
    /// sees the text <c>200K</c>.
    /// </summary>
    private static string WindowColor(string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var value = space < 0 ? text : text[(space + 1)..];
        return Theme.ForValue("context-window", value, null, WindowTokens(value));
    }

    private static double? WindowTokens(string value)
    {
        var scale = 1.0;
        var digits = value;
        if (digits.EndsWith('K') || digits.EndsWith('k'))
        {
            scale = 1_000;
            digits = digits[..^1];
        }
        else if (digits.EndsWith('M') || digits.EndsWith('m'))
        {
            scale = 1_000_000;
            digits = digits[..^1];
        }

        return double.TryParse(
            digits,
            System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture,
            out var count)
            ? count * scale
            : null;
    }

    private string PluginField(string name)
    {
        if (PluginFields is null
            || string.IsNullOrEmpty(name)
            || !PluginFields.TryGetValue(name, out var value)
            || string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var line = TerminalText.SanitizeLine(value);
        return TextWidth.Truncate(line, 64);
    }

    private StatusFieldRule? RuleFor(string field) =>
        StatusLineRules.FirstOrDefault(rule => rule.Field == field);

    private static PaintLine FitStatusLine(
        List<(string Plain, string Markup, string Slot)> items,
        int width,
        Func<List<(string Plain, string Markup, string Slot)>, int> indexOfDroppable)
    {
        const string SepPlain = "  ·  ";
        var sepMarkup = $"[{Theme.Rule}]  ·  [/]";

        var fullPlain = "  " + string.Join(SepPlain, items.Select(x => x.Plain));
        if (TextWidth.Measure(fullPlain) <= width)
        {
            var fullMarkup = "  " + string.Join(sepMarkup, items.Select(x => x.Markup));
            return new PaintLine(fullMarkup, fullPlain);
        }

        while (TextWidth.Measure("  " + string.Join(SepPlain, items.Select(x => x.Plain))) > width)
        {
            var dropIndex = indexOfDroppable(items);
            if (dropIndex < 0)
            {
                break;
            }

            items.RemoveAt(dropIndex);
        }

        var fittedPlain = "  " + string.Join(SepPlain, items.Select(x => x.Plain));
        if (TextWidth.Measure(fittedPlain) > width)
        {
            fittedPlain = TextWidth.Truncate(fittedPlain, width);
            return PaintLine.Colored(Theme.Chrome, fittedPlain);
        }

        var fittedMarkup = "  " + string.Join(sepMarkup, items.Select(x => x.Markup));
        return new PaintLine(fittedMarkup, fittedPlain);
    }

    /// <summary>
    /// Custom line order: fields in the drop order go first, then the rest from
    /// the end of the line. A keep field never goes.
    /// </summary>
    private int IndexOfCustomDroppable(List<(string Plain, string Markup, string Slot)> items)
    {
        bool Keeps(string field) => field == PendingSlot || RuleFor(field) is { Keep: true };
        foreach (var field in StatusLineDropOrder)
        {
            var index = items.FindIndex(item => item.Slot == field);
            if (index >= 0)
            {
                return index;
            }
        }

        return items.FindLastIndex(item => !Keeps(item.Slot));
    }

    private static int IndexOfDroppable(List<(string Plain, string Markup, string Slot)> items)
    {
        foreach (var slot in DropSlots)
        {
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Slot == slot)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public bool SpinnerDue(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(Progress)
        && (_lastSpinner == default || now - _lastSpinner >= ProgressSpinner.Interval);

    public void TickSpinner(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(Progress))
        {
            _spinnerFrame = 0;
            _lastSpinner = default;
            _progressStarted = default;
            _elapsedLabel = string.Empty;
            return;
        }

        if (_progressStarted == default)
        {
            _progressStarted = now;
        }

        _elapsedLabel = ProgressElapsed.Format(now - _progressStarted);

        if (_lastSpinner == default)
        {
            _lastSpinner = now;
            return;
        }

        if (now - _lastSpinner < ProgressSpinner.Interval)
        {
            return;
        }

        _spinnerFrame++;
        _lastSpinner = now;
    }

    public PaintLine ProgressLine(int width)
    {
        if (string.IsNullOrWhiteSpace(Progress))
        {
            return PaintLine.Blank;
        }

        var glyph = ProgressSpinner.Frame(_spinnerFrame);
        var prefix = "  " + glyph + "  ";
        var elapsed = string.IsNullOrEmpty(_elapsedLabel)
            ? ProgressElapsed.Format(TimeSpan.Zero)
            : _elapsedLabel;
        var suffix = " · " + elapsed;
        if (!string.IsNullOrWhiteSpace(TokenEstimate))
        {
            suffix += " · " + TokenEstimate;
        }
        var prefixWidth = TextWidth.Measure(prefix);
        var suffixWidth = TextWidth.Measure(suffix);
        if (prefixWidth + suffixWidth >= width)
        {
            return PaintLine.Colored(
                Theme.Accent,
                TextWidth.Truncate(prefix + suffix, width));
        }

        var caption = Progress;
        var captionBudget = width - prefixWidth - suffixWidth;
        if (TextWidth.Measure(caption) > captionBudget)
        {
            caption = TextWidth.Truncate(caption, captionBudget);
        }

        var plain = prefix + caption + suffix;
        var markup = "  "
            + $"[{Theme.Accent}]{MarkupText.Escape(glyph)}[/]  "
            + $"[{Theme.Accent} bold]{MarkupText.Escape(caption)}[/]"
            + $"[{Theme.Rule}] · [/]"
            + $"[{Theme.Muted}]{MarkupText.Escape(elapsed)}[/]";
        if (!string.IsNullOrWhiteSpace(TokenEstimate))
        {
            markup += $"[{Theme.Rule}] · [/][{Theme.Muted}]{MarkupText.Escape(TokenEstimate)}[/]";
        }

        return new PaintLine(markup, plain);
    }
}
