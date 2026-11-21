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
            Row("Latest", When(report.Latest))
        };
        lines.AddRange(TokenRows(report).Select(static row => Row(row.Label, row.Value)));
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

    public static IReadOnlyList<(string Label, string Value)> TokenRows(SessionStatsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<(string Label, string Value)>();
        if (report.TokenTrackedSessions is { } tracked)
        {
            rows.Add(("Token sessions", $"{Count(tracked)} / {Count(report.Sessions)}"));
        }

        if (report.ReasoningTrackedSessions is { } reasoning)
        {
            rows.Add(("Reasoning sessions", $"{Count(reasoning)} / {Count(report.Sessions)}"));
        }

        rows.AddRange(
        [
            ("Input tokens", Count(report.InputTokens)),
            ("Output tokens", Count(report.OutputTokens)),
            ("Reasoning tokens", Count(report.ReasoningTokens)),
            ("Total tokens", Count(report.TotalTokens)),
            ("Average per session", Count(report.AverageTokensPerSession)),
            ("Median per session", Count(report.MedianTokensPerSession))
        ]);
        return rows.AsReadOnly();
    }

    public static IReadOnlyList<(string Label, string Value)> CacheRows(SessionStatsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<(string Label, string Value)>();
        if (report.CacheWindowDays is { } days)
        {
            rows.Add(("Request window (days)", Count(days)));
            rows.Add(("Tracked sessions", Count(report.CacheTrackedSessions)));
        }
        else
        {
            rows.Add(("Tracked sessions", $"{Count(report.CacheTrackedSessions)} / {Count(report.Sessions)}"));
        }

        rows.AddRange(UsageRows(report.CacheUsage, report.CacheBreakdown));
        return rows.AsReadOnly();
    }

    /// <summary>
    /// Cache totals, then compact splits by scope and by model. A split appears only
    /// when it adds information: a child or auxiliary request, or more than one model.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> UsageRows(
        SessionCacheUsage? usage, IReadOnlyList<SessionCacheBreakdown> breakdown)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        var requests = usage?.RequestCount ?? 0;
        var rate = Percent(usage?.GetReadRatePercent());
        var missRate = Percent(usage?.GetMissRatePercent());
        var rows = new List<(string Label, string Value)>
        {
            ("Requests", Count(requests)),
            ("Cache reads", Covered(Count(usage?.ReadTokens), usage?.ReadReports ?? 0, requests)),
            ("Cache writes", Covered(Count(usage?.WriteTokens), usage?.WriteReports ?? 0, requests)),
            ("Cache misses", Covered(Count(usage?.GetMissTokens()), usage?.MissReports ?? 0, requests)),
            ("Cache read rate", Covered(rate, usage?.ComparableRequests ?? 0, requests)),
            ("Cache miss rate", Covered(missRate, usage?.ComparableRequests ?? 0, requests))
        };
        foreach (var writes in usage?.WritesByLifetime ?? [])
        {
            rows.Add(($"  {Count(writes.LifetimeSeconds)} s lifetime",
                Covered(Count(writes.Tokens), writes.Reports, requests)));
        }

        var scopes = breakdown
            .GroupBy(static row => row.Scope, StringComparer.Ordinal)
            .Select(static group => (Label: group.Key, Usage: Merge(group)))
            .ToList();
        if (scopes.Any(static scope => scope.Label != "Main"))
        {
            rows.Add(("By scope", string.Empty));
            rows.AddRange(scopes.Select(static scope => ($"  {scope.Label}", Share(scope.Usage))));
        }

        var models = breakdown
            .Where(static row => row.Model is not null)
            .GroupBy(static row => (row.Provider, row.Model))
            .Select(static group => (group.Key.Provider, Model: group.Key.Model!, Usage: Merge(group)))
            .OrderByDescending(static model => model.Usage.RequestCount)
            .ThenBy(static model => model.Model, StringComparer.Ordinal)
            .ToList();
        if (models.Count > 1)
        {
            // Provider only disambiguates a model name served by more than one provider.
            var shared = models.GroupBy(static model => model.Model).Where(static group => group.Count() > 1)
                .Select(static group => group.Key).ToHashSet(StringComparer.Ordinal);
            rows.Add(("By model", string.Empty));
            rows.AddRange(models.Select(model => (
                "  " + Fit(shared.Contains(model.Model) ? $"{model.Model} ({model.Provider})" : model.Model, 20),
                Share(model.Usage))));
        }

        return rows.AsReadOnly();
    }

    private static SessionCacheUsage Merge(IEnumerable<SessionCacheBreakdown> rows) =>
        rows.Select(static row => row.Usage).Aggregate(static (total, usage) => total.Add(usage));

    private static string Share(SessionCacheUsage usage) =>
        $"{Count(usage.RequestCount)} req, {Percent(usage.GetReadRatePercent())} read";

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : string.Concat(text.AsSpan(0, width - 2), "..");

    private static string Percent(decimal? value) =>
        value is { } percent ? percent.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";

    private static string Covered(string value, long reports, long requests) =>
        reports > 0 && reports < requests ? $"{value} ({Count(reports)}/{Count(requests)} requests)" : value;

    private static string Row(string label, string value) => $"  {label,-22}{value}";

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Count(long? value) => value is { } count ? Count(count) : "--";

    private static string When(DateTimeOffset? value) =>
        value is { } time ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "--";
}
