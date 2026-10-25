using System.Globalization;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Plain-text detail report rendered by <c>/status</c>.
/// </summary>
public static class StatusText
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
                ("Mode", ModeLabel.For(status.PlanMode, status.Mode)),
                ("Approval", ApprovalLabel.For(status.Approval)),
                ("Prompt set", status.PromptSet)
            ]);
        var modelRows = new List<(string Label, string Value)>
        {
            ("Provider", status.Provider),
            ("Model", status.Model),
            ("Thinking", ThinkingValue(status.Thinking))
        };
        AddSlot(modelRows, "Tier", status.ServiceTier);
        AddSlot(modelRows, "Speed", status.Speed);
        if (!string.IsNullOrWhiteSpace(status.ApprovalModel))
        {
            modelRows.Add(("Approval model", status.ApprovalModel));
        }

        if (status.ApprovalThinking is not null)
        {
            modelRows.Add(("Approval thinking", ThinkingValue(status.ApprovalThinking)));
        }

        if (!string.IsNullOrWhiteSpace(status.ExtendedApprovalModel))
        {
            modelRows.Add(("Extended approval", status.ExtendedApprovalModel));
        }

        if (status.ExtendedApprovalThinking is not null)
        {
            modelRows.Add(("Extended thinking", ThinkingValue(status.ExtendedApprovalThinking)));
        }

        if (!string.IsNullOrWhiteSpace(status.CompactionModel))
        {
            modelRows.Add(("Compaction model", status.CompactionModel));
        }

        if (!string.IsNullOrWhiteSpace(status.MicroCompaction))
        {
            modelRows.Add(("Microcompaction", status.MicroCompaction));
        }

        if (full)
        {
            if (!string.IsNullOrWhiteSpace(status.Retry))
            {
                modelRows.Add(("Retry", status.Retry));
            }

            if (!string.IsNullOrWhiteSpace(status.ApprovalRetry))
            {
                modelRows.Add(("Approval retry", status.ApprovalRetry));
            }

            if (!string.IsNullOrWhiteSpace(status.ExtendedApprovalTrigger))
            {
                modelRows.Add(("Extended trigger", status.ExtendedApprovalTrigger));
            }

            if (status.ExtendedApprovalPrimaryReview is bool primary)
            {
                modelRows.Add(("Extended primary review", Toggle(primary)));
            }
        }

        AddSection(lines, "Model", modelRows);
        AddSection(
            lines,
            "Session tokens",
            [
                ("Session input", TokenValue(status.CumulativeUsage?.InputTokenCount)),
                ("Session output", TokenValue(status.CumulativeUsage?.OutputTokenCount)),
                ("Session total", TokenValue(status.CumulativeUsage?.TotalTokenCount))
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
                ("MCP", Toggle(status.McpEnabled)),
                ("Plugins", Toggle(status.PluginsEnabled)),
                ("Global rules", Toggle(status.GlobalRulesEnabled)),
                ("Project rules", Toggle(status.ProjectRulesEnabled)),
                ("Estimated tokens", Toggle(status.EstimatedTokensEnabled)),
                ("Custom status line", Toggle(status.CustomStatusLineEnabled)),
                ("Verbose tools", Toggle(status.VerboseToolsEnabled)),
                ("Verbose commands", Toggle(status.VerboseCommandsEnabled)),
                ("Verbose results", Toggle(status.VerboseResultsEnabled)),
                ("Verbose calls", Toggle(status.VerboseCallsEnabled)),
                ("Verbose approvals", Toggle(status.VerboseApprovalsEnabled)),
                ("Verbose review", Toggle(status.VerboseReviewEnabled)),
                ("Verbose thinking", Toggle(status.VerboseThinkingEnabled))
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
                    ("External loaded", Number(status.ExternalTools)),
                    ("MCP servers", Number(status.McpServers)),
                    ("MCP tools", Number(status.McpTools)),
                    ("Plugins loaded", Number(status.Plugins))
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

    private static void AddSlot(List<(string Label, string Value)> rows, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        const string separator = " ";
        var split = value.IndexOf(separator, StringComparison.Ordinal);
        rows.Add((label, split < 0 ? value : value[(split + separator.Length)..]));
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
