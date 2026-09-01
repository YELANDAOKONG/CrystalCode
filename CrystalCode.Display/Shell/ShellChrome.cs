using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Persistent chrome fields for one session: status bar and progress row.
/// </summary>
public sealed class ShellChrome
{
    private static readonly string[] DropSlots = ["total", "elapsed", "model", "path"];
    private int _spinnerFrame;
    private DateTimeOffset _lastSpinner;
    private string _progress = string.Empty;
    private DateTimeOffset _progressStarted;
    private string _elapsedLabel = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public bool PlanMode { get; set; }

    public string Approval { get; set; } = string.Empty;

    public string Thinking { get; set; } = string.Empty;

    public string Usage { get; set; } = "CTX --";

    public string UsageTotal { get; set; } = string.Empty;

    public string Activity { get; set; } = string.Empty;

    public string Progress
    {
        get => _progress;
        set
        {
            var next = value ?? string.Empty;
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

    public string TokenEstimate { get; set; } = string.Empty;

    public int ToolCount { get; set; }

    public string Elapsed { get; set; } = string.Empty;

    public int Queued { get; set; }

    public PaintLine StatusLine(int width)
    {
        var items = new List<(string Plain, string Markup, string Slot)>();

        if (!string.IsNullOrWhiteSpace(Approval))
        {
            items.Add((Approval, $"[{Theme.Chrome}]{MarkupText.Escape(Approval)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Thinking))
        {
            items.Add((Thinking, $"[{Theme.Chrome}]{MarkupText.Escape(Thinking)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Activity))
        {
            var activityPlain = "• " + Activity;
            items.Add((activityPlain, $"[{Theme.Accent}]{MarkupText.Escape(activityPlain)}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Model))
        {
            items.Add((Model, $"[{Theme.User}]{MarkupText.Escape(Model)}[/]", "model"));
        }

        if (!string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            var shortPath = PathDisplay.Shorten(WorkspaceRoot);
            items.Add((shortPath, $"[{Theme.Muted}]{MarkupText.Escape(shortPath)}[/]", "path"));
        }

        var usageColor = Theme.Chrome;
        if (Usage.Contains('%', StringComparison.Ordinal))
        {
            var pctIndex = Usage.IndexOf('%', StringComparison.Ordinal);
            var spaceBefore = Usage.LastIndexOf(' ', pctIndex);
            if (spaceBefore >= 0 && int.TryParse(Usage[(spaceBefore + 1)..pctIndex], out var pct))
            {
                if (pct >= 80)
                {
                    usageColor = Theme.Fail;
                }
                else if (pct >= 60)
                {
                    usageColor = Theme.Warning;
                }
            }
        }

        items.Add((Usage, $"[{usageColor}]{MarkupText.Escape(Usage)}[/]", "keep"));
        if (!string.IsNullOrWhiteSpace(UsageTotal))
        {
            items.Add((UsageTotal, $"[{usageColor}]{MarkupText.Escape(UsageTotal)}[/]", "total"));
        }

        if (ToolCount > 0)
        {
            var toolStr = ToolCount == 1 ? "1 Tool" : $"{ToolCount} Tools";
            items.Add((toolStr, $"[{Theme.Chrome}]{toolStr}[/]", "keep"));
        }

        if (!string.IsNullOrWhiteSpace(Elapsed))
        {
            items.Add((Elapsed, $"[{Theme.Chrome}]{MarkupText.Escape(Elapsed)}[/]", "elapsed"));
        }

        if (Queued > 0)
        {
            var queueStr = $"Queued {Queued}";
            items.Add((queueStr, $"[{Theme.Warning} bold]{queueStr}[/]", "keep"));
        }

        const string SepPlain = "  ·  ";
        const string SepMarkup = $"[{Theme.Rule}]  ·  [/]";

        var fullPlain = "  " + string.Join(SepPlain, items.Select(x => x.Plain));
        if (TextWidth.Measure(fullPlain) <= width)
        {
            var fullMarkup = "  " + string.Join(SepMarkup, items.Select(x => x.Markup));
            return new PaintLine(fullMarkup, fullPlain);
        }

        while (TextWidth.Measure("  " + string.Join(SepPlain, items.Select(x => x.Plain))) > width)
        {
            var dropIndex = IndexOfDroppable(items);
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

        var fittedMarkup = "  " + string.Join(SepMarkup, items.Select(x => x.Markup));
        return new PaintLine(fittedMarkup, fittedPlain);
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
