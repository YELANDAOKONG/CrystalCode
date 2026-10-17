using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Providers.DeepSeek;

using Xunit;

namespace CrystalCode.Engine.Tests.Approvals;

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

    [Fact]
    public async Task ReviewAsync_RetriesTransientFailureThenParsesAssessment()
    {
        using var root = new TemporaryWorkspace();
        var client = new RetryingChatClient(
            new DeepSeekException("Internal server error", statusCode: 503),
            failures: 1,
            """
            {"outcome":"allow","risk_level":"low","user_authorization":"high","rationale":"ok"}
            """);
        var reviewer = new ModelApprovalReviewer(client, retry: InstantRetry());
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.True(verdict.IsAllow);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ReviewAsync_DoesNotRetryRejectedRequest()
    {
        using var root = new TemporaryWorkspace();
        var client = new RetryingChatClient(
            new DeepSeekException("Unauthorized", statusCode: 401),
            failures: 1,
            "unused");
        var reviewer = new ModelApprovalReviewer(client, retry: InstantRetry());
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.Equal("ask", verdict.Outcome);
        Assert.StartsWith("The approval reviewer failed:", verdict.Rationale, StringComparison.Ordinal);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ReviewAsync_AsksAfterRetriesAreExhausted()
    {
        using var root = new TemporaryWorkspace();
        var client = new RetryingChatClient(
            new DeepSeekException("Internal server error", statusCode: 503),
            failures: int.MaxValue,
            "unused");
        var reviewer = new ModelApprovalReviewer(client, retry: InstantRetry());
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.Equal("ask", verdict.Outcome);
        Assert.Equal(SessionRetryOptions.DefaultMaximumRetries + 1, client.Calls);
    }

    [Fact]
    public async Task ReviewAsync_PropagatesCancellation()
    {
        using var root = new TemporaryWorkspace();
        var client = new RetryingChatClient(
            new DeepSeekException("Internal server error", statusCode: 503),
            failures: 1,
            "unused");
        var reviewer = new ModelApprovalReviewer(client, retry: InstantRetry());
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await reviewer.ReviewAsync(
                new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."),
                cancellation.Token));
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task ReviewAsync_DoesNotRetryWhenRetriesAreDisabled()
    {
        using var root = new TemporaryWorkspace();
        var client = new RetryingChatClient(
            new DeepSeekException("Internal server error", statusCode: 503),
            failures: int.MaxValue,
            "unused");
        var reviewer = new ModelApprovalReviewer(client, retry: SessionRetryOptions.None);
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.Equal("ask", verdict.Outcome);
        Assert.Equal(1, client.Calls);
    }

    private static SessionRetryOptions InstantRetry() =>
        new(
            SessionRetryOptions.DefaultMaximumRetries,
            (_, _) => Task.CompletedTask,
            () => 0);

    private static ToolCall WriteCall() =>
        JsonToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");
}
