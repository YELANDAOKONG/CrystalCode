using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Persistent chrome fields for one session: status bar and progress row.
/// </summary>
public sealed class ShellChrome
{
    private int _spinnerFrame;
    private DateTimeOffset _lastSpinner;

    public string Model { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public bool PlanMode { get; set; }

    public string Approval { get; set; } = string.Empty;

    public string Thinking { get; set; } = string.Empty;

    public string Usage { get; set; } = "CTX --";

    public string Activity { get; set; } = string.Empty;

    public string Progress { get; set; } = string.Empty;

    public int ToolCount { get; set; }

    public string Elapsed { get; set; } = string.Empty;

    public int Queued { get; set; }

    public PaintLine StatusLine(int width)
    {
        var items = new List<(string Plain, string Markup)>();

        if (!string.IsNullOrWhiteSpace(Approval))
        {
            items.Add((Approval, $"[{Theme.Chrome}]{MarkupText.Escape(Approval)}[/]"));
        }

        if (!string.IsNullOrWhiteSpace(Thinking))
        {
            items.Add((Thinking, $"[{Theme.Chrome}]{MarkupText.Escape(Thinking)}[/]"));
        }

        if (!string.IsNullOrWhiteSpace(Activity))
        {
            var activityPlain = "• " + Activity;
            items.Add((activityPlain, $"[{Theme.Accent}]{MarkupText.Escape(activityPlain)}[/]"));
        }

        if (!string.IsNullOrWhiteSpace(Model))
        {
            items.Add((Model, $"[{Theme.User}]{MarkupText.Escape(Model)}[/]"));
        }

        if (!string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            var shortPath = PathDisplay.Shorten(WorkspaceRoot);
            items.Add((shortPath, $"[{Theme.Muted}]{MarkupText.Escape(shortPath)}[/]"));
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

        items.Add((Usage, $"[{usageColor}]{MarkupText.Escape(Usage)}[/]"));

        if (ToolCount > 0)
        {
            var toolStr = ToolCount == 1 ? "1 Tool" : $"{ToolCount} Tools";
            items.Add((toolStr, $"[{Theme.Chrome}]{toolStr}[/]"));
        }

        if (!string.IsNullOrWhiteSpace(Elapsed))
        {
            items.Add((Elapsed, $"[{Theme.Chrome}]{MarkupText.Escape(Elapsed)}[/]"));
        }

        if (Queued > 0)
        {
            var queueStr = $"Queued {Queued}";
            items.Add((queueStr, $"[{Theme.Warning} bold]{queueStr}[/]"));
        }

        const string SepPlain = "  ·  ";
        const string SepMarkup = $"[{Theme.Rule}]  ·  [/]";

        var fullPlain = "  " + string.Join(SepPlain, items.Select(x => x.Plain));
        if (TextWidth.Measure(fullPlain) <= width)
        {
            var fullMarkup = "  " + string.Join(SepMarkup, items.Select(x => x.Markup));
            return new PaintLine(fullMarkup, fullPlain);
        }

        while (items.Count > 3 && TextWidth.Measure("  " + string.Join(SepPlain, items.Select(x => x.Plain))) > width)
        {
            var dropIndex = -1;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Plain == Elapsed
                    || items[i].Plain.EndsWith(" Tool", StringComparison.Ordinal)
                    || items[i].Plain.EndsWith(" Tools", StringComparison.Ordinal)
                    || items[i].Plain == Model)
                {
                    dropIndex = i;
                    break;
                }
            }

            if (dropIndex < 0 && items.Count > 4)
            {
                dropIndex = 4;
            }

            if (dropIndex >= 0)
            {
                items.RemoveAt(dropIndex);
            }
            else
            {
                break;
            }
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

    public bool SpinnerDue(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(Progress)
        && (_lastSpinner == default || now - _lastSpinner >= ProgressSpinner.Interval);

    public void TickSpinner(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(Progress))
        {
            _spinnerFrame = 0;
            _lastSpinner = default;
            return;
        }

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
        var prefixWidth = TextWidth.Measure(prefix);
        if (prefixWidth >= width)
        {
            return PaintLine.Colored(Theme.Accent, TextWidth.Truncate(prefix, width));
        }

        var caption = Progress;
        var captionBudget = width - prefixWidth;
        if (TextWidth.Measure(caption) > captionBudget)
        {
            caption = TextWidth.Truncate(caption, captionBudget);
        }

        var plain = prefix + caption;
        var markup = "  "
            + $"[{Theme.Accent}]{MarkupText.Escape(glyph)}[/]  "
            + $"[{Theme.Accent} bold]{MarkupText.Escape(caption)}[/]";
        return new PaintLine(markup, plain);
    }
}
