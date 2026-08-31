using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Tests.Tools;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Approvals;

public sealed class ModelApprovalReviewerTests
{
    [Fact]
    public async Task ReviewAsync_ParsesCodexAssessment()
    {
        using var root = new TemporaryWorkspace();
        var client = new FixedChatClient(
            """
            {
              "outcome": "allow",
              "risk_level": "low",
              "user_authorization": "high",
              "rationale": "Adds the requested test."
            }
            """);
        var reviewer = new ModelApprovalReviewer(client);
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.True(verdict.IsAllow);
        Assert.Equal(ReviewRiskLevel.Low, verdict.RiskLevel);
        Assert.Equal(ReviewAuthorization.High, verdict.UserAuthorization);
        Assert.Equal("Adds the requested test.", verdict.Rationale);
        Assert.NotNull(client.LastRequest);
        Assert.Contains(
            client.LastRequest.Items.OfType<ChatMessage>(),
            message => message.Role == ChatRole.User
                && message.Text.Contains("## Conversation", StringComparison.Ordinal)
                && message.Text.Contains("Add a failing test.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewAsync_AsksWhenConversationIsMissing()
    {
        using var root = new TemporaryWorkspace();
        var client = new FixedChatClient("unused");
        var reviewer = new ModelApprovalReviewer(client);
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "  "));

        Assert.False(verdict.IsAllow);
        Assert.False(verdict.IsDeny);
        Assert.Equal("No conversation is available to review against.", verdict.Rationale);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public async Task ReviewAsync_AsksWhenAssessmentOmitsRisk()
    {
        using var root = new TemporaryWorkspace();
        var reviewer = new ModelApprovalReviewer(
            new FixedChatClient("""{"outcome":"allow","rationale":"ok"}"""));
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.Equal("ask", verdict.Outcome);
        Assert.Equal(
            "The approval reviewer did not return a usable assessment.",
            verdict.Rationale);
    }

    private static ToolCall WriteCall() =>
        new("1", WriteTool.ToolName, """{"path":"src/App.cs","contents":"x"}""");
}
