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

    [Fact]
    public void Render_DrawsAScrollbarOnlyWhenMoreSessionsThanRowsMatch()
    {
        var few = Sessions(2);
        var many = Sessions(40);

        var fewText = Draw(SessionPicker.Render(few, string.Empty, 0, null, false));
        var manyText = Draw(SessionPicker.Render(many, string.Empty, 0, null, false));

        Assert.DoesNotContain('\u2503', fewText);
        Assert.Contains('\u2503', manyText);
    }

    private static SessionSummary[] Sessions(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new SessionSummary($"id{i}", "/tmp/work", false, null, DateTimeOffset.UtcNow, 1, "Fix " + i))
            .ToArray();

    private static string Draw(Spectre.Console.Rendering.IRenderable widget)
    {
        var writer = new StringWriter();
        var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
        {
            Ansi = Spectre.Console.AnsiSupport.No,
            ColorSystem = Spectre.Console.ColorSystemSupport.NoColors,
            Out = new Spectre.Console.AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 100;
        console.Write(widget);
        return writer.ToString();
    }
}
