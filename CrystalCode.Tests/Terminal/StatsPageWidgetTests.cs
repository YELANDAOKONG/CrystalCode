using CrystalCode.Display.Paint;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class StatsPageWidgetTests
{
    [Fact]
    public void Create_RendersAlignedSectionsAndToolShareBar()
    {
        var report = new SessionStatsReport(
            2,
            4,
            5,
            10,
            1_200,
            340,
            20,
            1_560,
            780,
            780,
            7,
            new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 4, 8, 0, 0, TimeSpan.Zero),
            [new SessionToolCount("read", 6), new SessionToolCount("bash", 4)]);

        var lines = WidgetPaint.Plain(StatsPageWidget.Create(report, allWorkspaces: false), 80);
        var text = string.Join('\n', lines);

        Assert.Contains("This workspace", text, StringComparison.Ordinal);
        Assert.Contains("Overview", text, StringComparison.Ordinal);
        Assert.Contains("Tokens", text, StringComparison.Ordinal);
        Assert.Contains("Tools", text, StringComparison.Ordinal);
        Assert.Contains("1,560", text, StringComparison.Ordinal);
        Assert.Contains(lines, static line =>
            line.Contains("read", StringComparison.Ordinal)
            && line.Contains("60.0%", StringComparison.Ordinal)
            && line.Contains('█')
            && !line.Contains('░'));
        Assert.Contains("Esc or q to close", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShowsNoneWhenThereAreNoTools()
    {
        var report = new SessionStatsReport(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            []);

        var text = string.Join('\n', WidgetPaint.Plain(StatsPageWidget.Create(report, allWorkspaces: true), 80));

        Assert.Contains("All workspaces", text, StringComparison.Ordinal);
        Assert.Contains("None", text, StringComparison.Ordinal);
        Assert.DoesNotContain("█", text, StringComparison.Ordinal);
    }
}
