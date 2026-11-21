using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class StatsTextTests
{
    [Fact]
    public void TokenRows_ShowsCoverageAndUnknownValues()
    {
        var report = new SessionStatsReport(3, 1, 1, 0, 100, 50, null, 150, 75, 75, 0, null, null, [])
        {
            TokenTrackedSessions = 2,
            ReasoningTrackedSessions = 0
        };

        var rows = StatsText.TokenRows(report);

        Assert.Contains(("Token sessions", "2 / 3"), rows);
        Assert.Contains(("Reasoning sessions", "0 / 3"), rows);
        Assert.Contains(("Input tokens", "100"), rows);
        Assert.Contains(("Reasoning tokens", "--"), rows);
    }

    [Fact]
    public void CacheRows_ShowTheRequestWindowInsteadOfTheSessionRatio()
    {
        var report = new SessionStatsReport(5, 1, 1, 0, null, null, null, null, null, null, 7, null, null, [])
        {
            CacheTrackedSessions = 2,
            CacheWindowDays = 7
        };

        var rows = StatsText.CacheRows(report);

        Assert.Contains(("Request window (days)", "7"), rows);
        Assert.Contains(("Tracked sessions", "2"), rows);
    }

    [Fact]
    public void CacheRows_ShowRequestsMissesAndMissRate()
    {
        var report = new SessionStatsReport(1, 1, 1, 0, null, null, null, null, null, null, 7, null, null, [])
        {
            CacheTrackedSessions = 1,
            CacheUsage = new SessionCacheUsage
            {
                RequestCount = 2,
                ReadReports = 2,
                ReadTokens = 60,
                MissReports = 2,
                MissTokens = 40,
                ComparableRequests = 2,
                ComparableInputTokens = 100,
                ComparableReadTokens = 60
            }
        };

        var rows = StatsText.CacheRows(report);

        Assert.Contains(("Requests", "2"), rows);
        Assert.Contains(("Cache misses", "40"), rows);
        Assert.Contains(("Cache miss rate", "40.0%"), rows);
    }

    [Fact]
    public void UsageRows_SplitByScopeOnlyWhenAChildOrAuxiliaryRequestExists()
    {
        var main = new SessionCacheUsage { RequestCount = 2, ComparableRequests = 2, ComparableInputTokens = 100, ComparableReadTokens = 50 };
        var child = new SessionCacheUsage { RequestCount = 1, ComparableRequests = 1, ComparableInputTokens = 10, ComparableReadTokens = 10 };
        var mainOnly = main with { ByScope = [new SessionCacheScopeUsage("Main", main)] };
        var mixed = main with
        {
            ByScope = [new SessionCacheScopeUsage("Main", main), new SessionCacheScopeUsage("Child", child)]
        };

        Assert.DoesNotContain(StatsText.UsageRows(mainOnly), row => row.Label == "By scope");
        var rows = StatsText.UsageRows(mixed);
        Assert.Contains(("  Main", "2 req, 50.0% read"), rows);
        Assert.Contains(("  Child", "1 req, 100.0% read"), rows);
    }
}
