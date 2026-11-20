using System.Globalization;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Plain-text form of the <c>/stats</c> page: overview, tokens, and the most
/// used tools.
/// </summary>
public static class StatsText
{
    public static string Format(SessionStatsReport report, bool allWorkspaces)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "Stats  " + (allWorkspaces ? "All workspaces" : "This workspace"),
            Row("Sessions", Count(report.Sessions)),
            Row("User turns", Count(report.UserTurns)),
            Row("Model calls", Count(report.ModelCalls)),
            Row("Tool calls", Count(report.ToolCalls)),
            Row("Days", Count(report.WindowDays)),
            Row("Earliest", When(report.Earliest)),
            Row("Latest", When(report.Latest)),
            Row("Input tokens", Count(report.InputTokens)),
            Row("Output tokens", Count(report.OutputTokens)),
            Row("Reasoning tokens", Count(report.ReasoningTokens)),
            Row("Total tokens", Count(report.TotalTokens)),
            Row("Average per session", Count(report.AverageTokensPerSession)),
            Row("Median per session", Count(report.MedianTokensPerSession))
        };
        lines.Add("Prompt cache");
        lines.AddRange(CacheRows(report).Select(static row => Row(row.Label, row.Value)));
        if (report.TopTools.Count == 0)
        {
            lines.Add("No tool calls.");
        }
        else
        {
            lines.Add("Top tools");
            lines.AddRange(report.TopTools.Select(static tool => Row(tool.Name, Count(tool.Count))));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static IReadOnlyList<(string Label, string Value)> CacheRows(SessionStatsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var usage = report.CacheUsage;
        var requests = usage?.RequestCount ?? 0;
        var rate = report.CacheReadRatePercent is { } percent
            ? percent.ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : "--";
        var rows = new List<(string Label, string Value)>
        {
            ("Tracked sessions", $"{Count(report.CacheTrackedSessions)} / {Count(report.Sessions)}"),
            ("Cache reads", Covered(Count(usage?.ReadTokens), usage?.ReadReports ?? 0, requests)),
            ("Cache writes", Covered(Count(usage?.WriteTokens), usage?.WriteReports ?? 0, requests)),
            ("Cache read rate", Covered(rate, usage?.ComparableRequests ?? 0, requests))
        };
        foreach (var writes in usage?.WritesByLifetime ?? [])
        {
            rows.Add(($"  {Count(writes.LifetimeSeconds)} s lifetime",
                Covered(Count(writes.Tokens), writes.Reports, requests)));
        }

        return rows.AsReadOnly();
    }

    private static string Covered(string value, long reports, long requests) =>
        reports > 0 && reports < requests ? $"{value} ({Count(reports)}/{Count(requests)} requests)" : value;

    private static string Row(string label, string value) => $"  {label,-22}{value}";

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Count(long? value) => value is { } count ? Count(count) : "--";

    private static string When(DateTimeOffset? value) =>
        value is { } time ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "--";
}
