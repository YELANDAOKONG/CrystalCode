using Crystal;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class StatusWidgetTests
{
    [Fact]
    public void Create_RendersAsWidthBoundedTable()
    {
        var status = new SessionStatus(
            "session-3",
            DateTimeOffset.UnixEpoch,
            "/work",
            false,
            ApprovalMode.Review,
            "Think Medium",
            "default",
            "openai",
            "gpt-5",
            128_000,
            null,
            0,
            0,
            0,
            0,
            0,
            true,
            true,
            true,
            true,
            true,
            5,
            8,
            2,
            null);

        var lines = WidgetPaint.Lines(StatusWidget.Create(status, full: true), 72);

        Assert.Contains(lines, line => line.Plain.Contains("Status", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Workspace", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Tokens", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Options", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Total", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("|----------------|", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.True(TextWidth.Measure(line.Plain) <= 72));
    }

    [Fact]
    public void Create_ShowsContextProgressForReportedUsage()
    {
        var status = new SessionStatus(
            "session-4",
            DateTimeOffset.UnixEpoch,
            "/work",
            false,
            ApprovalMode.Review,
            "Think Medium",
            "default",
            "openai",
            "gpt-5",
            1_000,
            new TokenUsage(100, 20),
            1,
            1,
            0,
            0,
            0,
            true,
            true,
            false,
            true,
            true,
            5,
            8,
            2,
            new TokenUsage(500, 50));

        var text = string.Join('\n', WidgetPaint.Plain(StatusWidget.Create(status, full: false), 88));

        Assert.Contains("|##--------------|", text, StringComparison.Ordinal);
        Assert.Contains("Window", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
        Assert.Contains("550", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval model", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShowsTheApprovalModelWhenSet()
    {
        var status = new SessionStatus(
            "session-5",
            DateTimeOffset.UnixEpoch,
            "/work",
            false,
            ApprovalMode.Review,
            "Think Medium",
            "default",
            "openai",
            "gpt-5",
            1_000,
            null,
            0,
            0,
            0,
            0,
            0,
            true,
            true,
            false,
            true,
            true,
            5,
            8,
            0,
            null,
            ApprovalModel: "openai / gpt-5.6-sol",
            ApprovalThinking: "Think High");

        var text = string.Join('\n', WidgetPaint.Plain(StatusWidget.Create(status, full: false), 88));

        Assert.Contains("Approval model", text, StringComparison.Ordinal);
        Assert.Contains("openai /", text, StringComparison.Ordinal);
        Assert.Contains("gpt-5.6-sol", text, StringComparison.Ordinal);
        Assert.Contains("Approval thinking", text, StringComparison.Ordinal);
        Assert.Contains("High", text, StringComparison.Ordinal);
    }
}
