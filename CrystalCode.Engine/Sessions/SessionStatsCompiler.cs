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
        var reasoningTrackedSessions = 0;
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
            foreach (var item in (session.Archive ?? session.Items).Concat(session.Children.SelectMany(child => child.Archive)))
            {
                if (!string.Equals(item.Kind, "tool_call", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                toolUsage[item.Name] = toolUsage.GetValueOrDefault(item.Name) + 1;
            }

            var usage = SessionMapper.ReadUsage(session.CumulativeUsage
                ?? (session.ModelCalls == 1 ? session.Usage : null));
            if (usage is not null)
            {
                input += usage.InputTokenCount;
                output += usage.OutputTokenCount;
                totals.Add(usage.TotalTokenCount);
                if (usage.ReasoningTokenCount is { } reasoned)
                {
                    reasoning += reasoned;
                    reasoningTrackedSessions++;
                }
            }

            var timestamp = session.UpdatedUtc ?? session.CreatedUtc;
            if (timestamp is null)
            {
                continue;
            }

            earliest = earliest is null || timestamp < earliest ? timestamp : earliest;
            latest = latest is null || timestamp > latest ? timestamp : latest;
        }

        totals.Sort();
        long? median = totals.Count switch
        {
            0 => null,
            _ when totals.Count % 2 == 1 => totals[totals.Count / 2],
            _ => totals[(totals.Count / 2) - 1]
                + (totals[totals.Count / 2] - totals[(totals.Count / 2) - 1]) / 2
        };
        var sessionsCount = considered.Count;
        long? total = totals.Count == 0 ? null : input + output;
        var average = totals.Count == 0 ? null : total / totals.Count;
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
        var (cacheTrackedSessions, cacheUsage) = CompileCache(sessions, cutoff, now);
        return new SessionStatsReport(
            sessionsCount,
            userTurns,
            modelCalls,
            toolCalls,
            totals.Count == 0 ? null : input,
            totals.Count == 0 ? null : output,
            reasoningTrackedSessions == 0 ? null : reasoning,
            total,
            average,
            median,
            rangeDays,
            earliest,
            latest,
            top)
        {
            TokenTrackedSessions = totals.Count,
            ReasoningTrackedSessions = reasoningTrackedSessions,
            CacheTrackedSessions = cacheTrackedSessions,
            CacheWindowDays = windowDays,
            CacheUsage = cacheUsage
        };
    }

    private static (int TrackedSessions, SessionCacheUsage? Usage) CompileCache(
        IReadOnlyList<SessionDocument> sessions, DateTimeOffset? cutoff, DateTimeOffset now)
    {
        var trackedSessions = 0;
        SessionCacheUsage? usage = null;
        foreach (var session in sessions)
        {
            var cache = session.CacheUsage;
            if (cutoff is { } start)
            {
                cache = null;
                foreach (var sample in session.CacheUsageSamples ?? [])
                {
                    if (sample.StartedUtc >= start && sample.StartedUtc <= now)
                    {
                        cache = (cache ?? new SessionCacheUsage()).Add(sample);
                    }
                }
            }

            if (cache is { RequestCount: > 0 })
            {
                trackedSessions++;
                usage = usage is null ? cache : usage.Add(cache);
            }
        }

        return (trackedSessions, usage);
    }
}
