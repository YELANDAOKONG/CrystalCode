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
}
