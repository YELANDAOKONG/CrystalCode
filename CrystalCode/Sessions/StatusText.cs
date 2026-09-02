using System.Globalization;

using CrystalCode.Approvals;
using CrystalCode.Display.Paint;

namespace CrystalCode.Sessions;

/// <summary>
/// Plain-text detail report rendered by <c>/status</c>.
/// </summary>
internal static class StatusText
{
    public static string Format(SessionStatus status, bool full)
    {
        ArgumentNullException.ThrowIfNull(status);

        var lines = new List<string> { full ? "Status · Full" : "Status", string.Empty };
        AddSection(
            lines,
            "Workspace",
            [
                ("Path", status.WorkspaceRoot),
                ("Mode", ModeLabel.For(status.PlanMode)),
                ("Approval", ApprovalLabel.For(status.Approval)),
                ("Prompt set", status.PromptSet)
            ]);
        AddSection(
            lines,
            "Model",
            [
                ("Provider", status.Provider),
                ("Model", status.Model),
                ("Thinking", ThinkingValue(status.Thinking))
            ]);
        AddSection(
            lines,
            "Tokens - cumulative",
            [
                ("Input tokens", TokenValue(status.CumulativeUsage?.InputTokenCount)),
                ("Output tokens", TokenValue(status.CumulativeUsage?.OutputTokenCount)),
                ("Total tokens", TokenValue(status.CumulativeUsage?.TotalTokenCount))
            ]);
        AddSection(
            lines,
            "Current context",
            [
                ("Window", Number(status.ContextWindow)),
                ("Usage", ContextValue(status.Usage?.TotalTokenCount, status.ContextWindow))
            ]);
        AddSection(
            lines,
            "Options",
            [
                ("Skills", Toggle(status.SkillsEnabled)),
                ("External tools", Toggle(status.ExternalToolsEnabled)),
                ("Estimated tokens", Toggle(status.EstimatedTokensEnabled)),
                ("Verbose tools", Toggle(status.VerboseToolsEnabled)),
                ("Verbose commands", Toggle(status.VerboseCommandsEnabled))
            ],
            trailingBlankLine: full);

        if (full)
        {
            AddSection(
                lines,
                "Latest request",
                [
                    ("Input tokens", TokenValue(status.Usage?.InputTokenCount)),
                    ("Output tokens", TokenValue(status.Usage?.OutputTokenCount)),
                    ("Total tokens", TokenValue(status.Usage?.TotalTokenCount))
                ]);
            AddSection(
                lines,
                "Activity",
                [
                    ("Session ID", status.SessionId),
                    ("Started", status.StartedUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)),
                    ("Turns", Number(status.UserTurns)),
                    ("Model calls", Number(status.ModelCalls)),
                    ("Tool calls", Number(status.ToolCalls)),
                    ("Queued messages", Number(status.QueuedMessages)),
                    ("Todos", Number(status.Todos))
                ]);
            AddSection(
                lines,
                "Tools",
                [
                    ("Plan tools", Number(status.PlanTools)),
                    ("Work tools", Number(status.WorkTools)),
                    ("External loaded", Number(status.ExternalTools))
                ],
                trailingBlankLine: false);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddSection(
        List<string> lines,
        string title,
        IReadOnlyList<(string Label, string Value)> rows,
        bool trailingBlankLine = true)
    {
        lines.Add(title);
        var width = rows.Max(row => row.Label.Length);
        foreach (var (label, value) in rows)
        {
            lines.Add($"  {label.PadRight(width)}  {value}");
        }

        if (trailingBlankLine)
        {
            lines.Add(string.Empty);
        }
    }

    private static string ThinkingValue(string thinking)
    {
        const string prefix = "Think ";
        if (string.IsNullOrWhiteSpace(thinking))
        {
            return "Unavailable";
        }

        return thinking.StartsWith(prefix, StringComparison.Ordinal)
            ? thinking[prefix.Length..]
            : thinking;
    }

    private static string ContextValue(long? used, int contextWindow)
    {
        var capacity = Number(contextWindow);
        if (used is null)
        {
            return $"-- / {capacity}";
        }

        var percent = contextWindow <= 0
            ? 0
            : Math.Clamp((int)(used.Value * 100 / contextWindow), 0, 100);
        return $"{Number(used.Value)} / {capacity} ({percent}%)";
    }

    private static string TokenValue(long? value) => value is null ? "--" : Number(value.Value);

    private static string Toggle(bool enabled) => enabled ? "On" : "Off";

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
