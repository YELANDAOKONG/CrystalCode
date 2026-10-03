using System.Globalization;

namespace CrystalCode.Engine.Sessions;

internal static class SessionStatsText
{
    public static string Format(SessionStatsReport stats, bool includeAllWorkspaces)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var lines = new List<string>
        {
            includeAllWorkspaces ? "Stats · All workspaces" : "Stats · This workspace",
            string.Empty,
            "Overview",
            "  Sessions               " + Number(stats.Sessions),
            "  User turns             " + Number(stats.UserTurns),
            "  Model calls            " + Number(stats.ModelCalls),
            "  Tool calls             " + Number(stats.ToolCalls),
            "  Window days            " + Number(stats.WindowDays),
            "  Earliest               " + LocalDate(stats.Earliest),
            "  Latest                 " + LocalDate(stats.Latest),
            string.Empty,
            "Tokens",
            "  Input                  " + Number(stats.InputTokens),
            "  Output                 " + Number(stats.OutputTokens),
            "  Reasoning              " + Number(stats.ReasoningTokens),
            "  Total                  " + Number(stats.TotalTokens),
            "  Avg / session          " + Number(stats.AverageTokensPerSession),
            "  Median / session       " + Number(stats.MedianTokensPerSession),
            string.Empty,
            "Top tools"
        };

        if (stats.TopTools.Count == 0)
        {
            lines.Add("  None");
        }
        else
        {
            for (var index = 0; index < stats.TopTools.Count; index++)
            {
                var item = stats.TopTools[index];
                lines.Add(
                    "  " + (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(2)
                    + ". "
                    + item.Name
                    + "  "
                    + Number(item.Count));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string LocalDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "--";

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
