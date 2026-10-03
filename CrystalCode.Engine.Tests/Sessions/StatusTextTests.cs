using Crystal;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

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
            VerboseToolsEnabled: true,
            VerboseCommandsEnabled: false,
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
        Assert.Contains("Session total   1,200", text, StringComparison.Ordinal);
        Assert.Contains("Latest request", text, StringComparison.Ordinal);
        Assert.Contains("Model calls      4", text, StringComparison.Ordinal);
        Assert.Contains("External loaded  2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval model", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IncludesTheApprovalModelOnlyWhenSet()
    {
        var status = new SessionStatus(
            SessionId: "session-1",
            StartedUtc: DateTimeOffset.UnixEpoch,
            WorkspaceRoot: "/work",
            PlanMode: false,
            Approval: ApprovalMode.Review,
            Thinking: "Think Medium",
            PromptSet: "default",
            Provider: "openai",
            Model: "gpt-5",
            ContextWindow: 1_000,
            Usage: null,
            UserTurns: 0,
            ModelCalls: 0,
            ToolCalls: 0,
            QueuedMessages: 0,
            Todos: 0,
            SkillsEnabled: true,
            ExternalToolsEnabled: true,
            EstimatedTokensEnabled: false,
            VerboseToolsEnabled: true,
            VerboseCommandsEnabled: true,
            PlanTools: 1,
            WorkTools: 1,
            ExternalTools: 0,
            CumulativeUsage: null,
            ApprovalModel: "openai / gpt-5.6-sol");

        var text = StatusText.Format(status, full: false);

        Assert.Contains("Approval model  openai / gpt-5.6-sol", text, StringComparison.Ordinal);
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
            true,
            true,
            4,
            6,
            0,
            null);

        var text = StatusText.Format(status, full: false);

        Assert.Contains("Thinking  Unavailable", text, StringComparison.Ordinal);
        Assert.Contains("Usage   -- / 128,000", text, StringComparison.Ordinal);
        Assert.Contains("Session input   --", text, StringComparison.Ordinal);
        Assert.Contains("External tools", text, StringComparison.Ordinal);
        Assert.Contains("Custom status line", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Session ID", text, StringComparison.Ordinal);
    }
}
