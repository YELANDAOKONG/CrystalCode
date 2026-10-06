using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Sessions;

internal static class SessionStatsCompiler
{
    public static SessionStatsReport Compile(
        IReadOnlyList<SessionDocument> sessions,
        DateTimeOffset now,
        int? windowDays,
        int topTools)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (topTools <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topTools), topTools, "Top tools must be positive.");
        }

        var cutoff = windowDays is int days ? now.AddDays(-days) : (DateTimeOffset?)null;
        var considered = new List<SessionDocument>(sessions.Count);
        foreach (var session in sessions)
        {
            var timestamp = session.UpdatedUtc ?? session.CreatedUtc;
            if (cutoff is not null && timestamp is not null && timestamp < cutoff.Value)
            {
                continue;
            }

            considered.Add(session);
        }

        var toolUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        var totals = new List<long>(considered.Count);
        var input = 0L;
        var output = 0L;
        var reasoning = 0L;
        var userTurns = 0;
        var modelCalls = 0;
        var toolCalls = 0;
        DateTimeOffset? earliest = null;
        DateTimeOffset? latest = null;
        foreach (var session in considered)
        {
            userTurns += Math.Max(0, session.UserTurns);
            modelCalls += Math.Max(0, session.ModelCalls);
            toolCalls += Math.Max(0, session.ToolCalls);
            foreach (var item in session.Archive ?? session.Items)
            {
                if (!string.Equals(item.Kind, "tool_call", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                toolUsage[item.Name] = toolUsage.GetValueOrDefault(item.Name) + 1;
            }

            var usage = session.CumulativeUsage ?? session.Usage;
            var sessionInput = Math.Max(0, usage?.InputTokenCount ?? 0);
            var sessionOutput = Math.Max(0, usage?.OutputTokenCount ?? 0);
            var sessionReasoning = Math.Max(0, usage?.ReasoningTokenCount ?? 0);
            input += sessionInput;
            output += sessionOutput;
            reasoning += sessionReasoning;
            totals.Add(sessionInput + sessionOutput + sessionReasoning);

            var timestamp = session.UpdatedUtc ?? session.CreatedUtc;
            if (timestamp is null)
            {
                continue;
            }

            earliest = earliest is null || timestamp < earliest ? timestamp : earliest;
            latest = latest is null || timestamp > latest ? timestamp : latest;
        }

        totals.Sort();
        var median = totals.Count switch
        {
            0 => 0L,
            _ when totals.Count % 2 == 1 => totals[totals.Count / 2],
            _ => (totals[totals.Count / 2] + totals[(totals.Count / 2) - 1]) / 2
        };
        var sessionsCount = considered.Count;
        var total = input + output + reasoning;
        var average = sessionsCount == 0 ? 0 : total / sessionsCount;
        var top = toolUsage
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(topTools)
            .Select(static pair => new SessionToolCount(pair.Key, pair.Value))
            .ToArray();
        var rangeDays = windowDays
            ?? (earliest is not null && latest is not null
                ? Math.Max(1, (int)Math.Ceiling((latest.Value - earliest.Value).TotalDays))
                : 0);
        return new SessionStatsReport(
            sessionsCount,
            userTurns,
            modelCalls,
            toolCalls,
            input,
            output,
            reasoning,
            total,
            average,
            median,
            rangeDays,
            earliest,
            latest,
            top);
    }
}
