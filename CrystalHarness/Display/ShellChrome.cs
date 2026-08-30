namespace CrystalHarness.Display;

/// <summary>
/// Persistent status-bar fields for one session.
/// </summary>
public sealed class ShellChrome
{
    public string Model { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public bool PlanMode { get; set; }

    public string Approval { get; set; } = string.Empty;

    public string Usage { get; set; } = "ctx --";

    public string Activity { get; set; } = string.Empty;

    public int ToolCount { get; set; }

    public string Elapsed { get; set; } = string.Empty;

    public PaintLine StatusLine(int width)
    {
        var plain = Format();
        var padded = "  " + plain;
        if (TextWidth.Measure(padded) > width)
        {
            padded = TextWidth.Truncate(padded, width);
        }

        return PaintLine.Colored(Theme.Chrome, padded);
    }

    private string Format()
    {
        var mode = ModeLabel.For(PlanMode);
        var parts = new List<string> { mode };
        if (!string.IsNullOrWhiteSpace(Approval))
        {
            parts.Add(Approval);
        }

        if (!string.IsNullOrWhiteSpace(Activity))
        {
            parts.Add(Activity);
        }

        if (!string.IsNullOrWhiteSpace(Model))
        {
            parts.Add(Model);
        }

        if (!string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            parts.Add(PathDisplay.Shorten(WorkspaceRoot));
        }

        parts.Add(Usage);
        if (ToolCount > 0)
        {
            parts.Add(ToolCount + " tools");
        }

        if (!string.IsNullOrWhiteSpace(Elapsed))
        {
            parts.Add(Elapsed);
        }

        return string.Join("  ·  ", parts);
    }
}
