using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

using Xunit;

using ApprovalContext = CrystalCode.Engine.Tests.Approvals.ApprovalPolicyTests.ApprovalContext;

namespace CrystalCode.Engine.Tests.Approvals;

public sealed class ExtendedApprovalPolicyTests
{
    [Fact]
    public async Task DecideAsync_ExtendedAllow_ExecutesWithBothVerdicts()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var primary = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("the user asked for it"));
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("nothing unsafe"));
        var policy = context.CreatePolicy(prompt, primary, extended: Extended(extended));

        var decision = await policy.DecideAsync(BashCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal([ApprovalReviewStage.Primary, ApprovalReviewStage.Extended], prompt.ReviewStages);
        Assert.Equal(ApprovalPassReason.Extended, prompt.LastPassReason);
        Assert.Equal("the user asked for it", prompt.LastReview?.Rationale);
        Assert.Equal("nothing unsafe", prompt.LastExtendedReview?.Rationale);
        Assert.NotNull(extended.LastRequest);
        Assert.Equal("the user asked for it", extended.LastRequest.PrimaryReview?.Rationale);
        Assert.Equal(primary.LastRequest?.Conversation, extended.LastRequest.Conversation);
    }

    [Fact]
    public async Task DecideAsync_ExtendedDeny_RejectsWithItsRationale()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("ok")),
            extended: Extended(new FixedApprovalReviewer(ApprovalReviewVerdict.Deny("would print a secret"))));

        var decision = await policy.DecideAsync(BashCall());

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(0, prompt.PassCount);
        Assert.StartsWith(
            "The extended approval reviewer declined this action: would print a secret",
            decision.RejectionOutput?.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecideAsync_ExtendedAsk_ReachesTheOperatorWithBothVerdicts()
    {
        using var context = new ApprovalContext(ApprovalMode.Audit);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("ok")),
            extended: Extended(new FixedApprovalReviewer(ApprovalReviewVerdict.AskUser("touches a shared host"))));

        var decision = await policy.DecideAsync(BashCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(0, prompt.PassCount);
        Assert.Equal("ok", prompt.LastReview?.Rationale);
        Assert.Equal("touches a shared host", prompt.LastExtendedReview?.Rationale);
    }

    [Theory]
    [InlineData("low", "low", true)]
    [InlineData("medium", "low", false)]
    [InlineData("medium", "medium", true)]
    [InlineData("medium", "high", true)]
    [InlineData("high", "medium", false)]
    public async Task DecideAsync_Trigger_RunsOnlyAtOrAboveThePrimaryRisk(
        string trigger,
        string primaryRisk,
        bool runs)
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var primary = new FixedApprovalReviewer(new ApprovalReviewVerdict(
            "allow", new ReviewRiskLevel(primaryRisk), ReviewAuthorization.High, "asked"));
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("fine"));
        var policy = context.CreatePolicy(
            prompt,
            primary,
            extended: new ExtendedApprovalReviewer(extended, new ReviewRiskLevel(trigger), includePrimaryReview: true));

        var decision = await policy.DecideAsync(BashCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(runs, extended.LastRequest is not null);
        Assert.Equal(runs ? ApprovalPassReason.Extended : ApprovalPassReason.Review, prompt.LastPassReason);
    }

    [Fact]
    public async Task DecideAsync_IncludePrimaryOff_SendsNoPrimaryVerdict()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("fine"));
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("ok")),
            extended: new ExtendedApprovalReviewer(extended, ReviewRiskLevel.Low, includePrimaryReview: false));

        await policy.DecideAsync(BashCall());

        Assert.NotNull(extended.LastRequest);
        Assert.Null(extended.LastRequest.PrimaryReview);
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("ask")]
    public async Task DecideAsync_PrimaryNotAllow_SkipsTheExtendedReviewer(string outcome)
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("should not run"));
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(new ApprovalReviewVerdict(
                outcome, ReviewRiskLevel.Medium, ReviewAuthorization.Low, "no")),
            extended: Extended(extended));

        await policy.DecideAsync(BashCall());

        Assert.Null(extended.LastRequest);
        Assert.Equal([ApprovalReviewStage.Primary], prompt.ReviewStages);
        Assert.Null(prompt.LastExtendedReview);
    }

    [Fact]
    public async Task DecideAsync_Forbidden_SkipsTheExtendedReviewerAndAsks()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("should not run"));
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("looks fine")),
            extended: Extended(extended));

        await policy.DecideAsync(JsonToolCall("1", BashTool.ToolName, """{"command":"sudo ls"}"""));

        Assert.Null(extended.LastRequest);
        Assert.Equal(1, prompt.Count);
    }

    [Fact]
    public async Task DecideAsync_RequirePrompt_SkipsTheExtendedReviewerAndAsks()
    {
        using var context = new ApprovalContext(ApprovalMode.Audit);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("should not run"));
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("fine")),
            advise: static (_, classification) => classification with { RequirePrompt = true },
            extended: Extended(extended));

        await policy.DecideAsync(BashCall());

        Assert.Null(extended.LastRequest);
        Assert.Equal(1, prompt.Count);
    }

    [Fact]
    public async Task DecideAsync_PolicyPass_DoesNotReachEitherReviewer()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var extended = new FixedApprovalReviewer(ApprovalReviewVerdict.Deny("should not run"));
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Deny("should not run")),
            extended: Extended(extended));

        var decision = await policy.DecideAsync(
            JsonToolCall("1", WriteTool.ToolName, """{"path":"src/App.cs","contents":"x"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.ReviewCount);
        Assert.Null(extended.LastRequest);
    }

    private static ExtendedApprovalReviewer Extended(FixedApprovalReviewer reviewer) =>
        new(reviewer, ReviewRiskLevel.Low, includePrimaryReview: true);

    private static ToolCall BashCall() =>
        JsonToolCall("1", BashTool.ToolName, """{"command":"dotnet test"}""");
}
