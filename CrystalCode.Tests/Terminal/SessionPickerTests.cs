using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;
using CrystalCode.Terminal;

namespace CrystalCode.Tests.Terminal;

public sealed class SessionPickerTests
{
    [Fact]
    public void Filter_PreservesUpdateOrderAndMatchesIdOrPreview()
    {
        SessionSummary[] sessions =
        [
            new("newest", "/tmp/work", false, null, DateTimeOffset.UtcNow, 2, "Fix parser"),
            new("older", "/tmp/work", false, null, DateTimeOffset.UtcNow.AddDays(-1), 1, "Fix tests"),
            new("oldest", "/tmp/work", false, null, DateTimeOffset.UtcNow.AddDays(-2), 1, "Review")
        ];

        var matches = SessionPicker.Filter(sessions, "fix");
        var byId = SessionPicker.Filter(sessions, "oldest");

        Assert.Equal(["newest", "older"], matches.Select(session => session.Id));
        Assert.Equal("oldest", Assert.Single(byId).Id);
    }

    [Fact]
    public void Filter_MatchesWorkspacePath()
    {
        SessionSummary[] sessions =
        [
            new("one", "/tmp/alpha", false, null, DateTimeOffset.UtcNow, 1, "Fix"),
            new("two", "/tmp/beta", false, null, DateTimeOffset.UtcNow, 1, "Fix")
        ];

        var matches = SessionPicker.Filter(sessions, "beta");

        Assert.Equal("two", Assert.Single(matches).Id);
    }
}
