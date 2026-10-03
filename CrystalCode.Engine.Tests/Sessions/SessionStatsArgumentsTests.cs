using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionStatsArgumentsTests
{
    [Fact]
    public void TryParse_DefaultsWhenNoArgumentIsGiven()
    {
        var ok = SessionStatsArguments.TryParse(string.Empty, out var parsed, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.False(parsed.IncludeAllWorkspaces);
        Assert.Null(parsed.WindowDays);
        Assert.Equal(SessionStatsArguments.DefaultTopTools, parsed.TopTools);
    }

    [Fact]
    public void TryParse_ReadsScopeWindowAndTopTools()
    {
        var ok = SessionStatsArguments.TryParse("all 30d tools 7", out var parsed, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.True(parsed.IncludeAllWorkspaces);
        Assert.Equal(30, parsed.WindowDays);
        Assert.Equal(7, parsed.TopTools);
    }

    [Fact]
    public void TryParse_RejectsUnknownOrInvalidTokens()
    {
        Assert.False(SessionStatsArguments.TryParse("tools", out _, out var missingCount));
        Assert.Equal("Pass a positive number after tools.", missingCount);

        Assert.False(SessionStatsArguments.TryParse("14x", out _, out var unknown));
        Assert.Equal(
            "Stats command supports: /stats, /stats all, /stats <Nd>, /stats tools <count>.",
            unknown);
    }
}
