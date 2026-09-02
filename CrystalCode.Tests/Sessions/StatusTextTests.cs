using Crystal;

using CrystalCode.Approvals;
using CrystalCode.Display.Paint;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class StatusTextTests
{
    [Fact]
    public void Format_IncludesDetailedSessionModelActivityAndCapabilities()
    {
        var status = new SessionStatus(
            SessionId: "session-1",
            StartedUtc: new DateTimeOffset(2026, 9, 2, 3, 4, 5, TimeSpan.Zero),
            WorkspaceRoot: "/work/crystal",
            PlanMode: false,
            Approval: ApprovalMode.Review,
            Thinking: "Think Medium",
            PromptSet: "focused",
            Provider: "openai",
            Model: "gpt-5",
            ContextWindow: 1_000,
            Usage: new TokenUsage(100, 20),
            UserTurns: 3,
            ModelCalls: 4,
            ToolCalls: 5,
            QueuedMessages: 1,
            Todos: 2,
            SkillsEnabled: true,
            ExternalToolsEnabled: true,
            EstimatedTokensEnabled: false,
            PlanTools: 7,
            WorkTools: 8,
            ExternalTools: 2,
            CumulativeUsage: new TokenUsage(1_000, 200));

        var text = StatusText.Format(status, full: true);

        Assert.Contains("Session ID       session-1", text, StringComparison.Ordinal);
        Assert.Contains("Started          2026-09-02 03:04:05 UTC", text, StringComparison.Ordinal);
        Assert.Contains("Mode        Work", text, StringComparison.Ordinal);
        Assert.Contains("Approval    Review", text, StringComparison.Ordinal);
        Assert.Contains("Thinking  Medium", text, StringComparison.Ordinal);
        Assert.Contains("Prompt set  focused", text, StringComparison.Ordinal);
        Assert.Contains("Provider  openai", text, StringComparison.Ordinal);
        Assert.Contains("Usage   120 / 1,000 (12%)", text, StringComparison.Ordinal);
        Assert.Contains("Total tokens   1,200", text, StringComparison.Ordinal);
        Assert.Contains("Latest request", text, StringComparison.Ordinal);
        Assert.Contains("Model calls      4", text, StringComparison.Ordinal);
        Assert.Contains("External loaded  2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ShowsMissingUsageAndUnavailableThinking()
    {
        var status = new SessionStatus(
            "session-2",
            DateTimeOffset.UnixEpoch,
            "/work",
            true,
            ApprovalMode.Default,
            string.Empty,
            "default",
            "deepseek",
            "deepseek-chat",
            128_000,
            null,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            false,
            4,
            6,
            0,
            null);

        var text = StatusText.Format(status, full: false);

        Assert.Contains("Thinking  Unavailable", text, StringComparison.Ordinal);
        Assert.Contains("Usage   -- / 128,000", text, StringComparison.Ordinal);
        Assert.Contains("Input tokens   --", text, StringComparison.Ordinal);
        Assert.Contains("External tools    Off", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Session ID", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Widget_RendersAsWidthBoundedTable()
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
    public void Widget_ShowsContextProgressForReportedUsage()
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
    }
}
