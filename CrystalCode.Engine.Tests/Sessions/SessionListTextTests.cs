using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionListTextTests
{
    [Fact]
    public void Format_MarksCurrentAndIncludesWorkspaceWhenRequested()
    {
        var sessions = new[]
        {
            new SessionSummary(
                "current",
                "/tmp/one",
                false,
                null,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                2,
                "fix the tests")
        };

        var text = SessionListText.Format(sessions, "current", includeWorkspace: true);

        Assert.Contains("* current", text, StringComparison.Ordinal);
        Assert.Contains("Work", text, StringComparison.Ordinal);
        Assert.Contains("2 turns", text, StringComparison.Ordinal);
        Assert.Contains("/tmp/one", text, StringComparison.Ordinal);
        Assert.Contains("fix the tests", text, StringComparison.Ordinal);
    }
}
