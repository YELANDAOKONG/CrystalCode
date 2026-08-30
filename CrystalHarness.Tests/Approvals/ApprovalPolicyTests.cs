using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Tests.Home;
using CrystalHarness.Tests.Tools;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Approvals;

public sealed class ApprovalPolicyTests
{
    [Fact]
    public async Task DecideAsync_Default_AutoExecutesRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        var policy = context.CreatePolicy(new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, """{"path":"a.txt"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
    }

    [Fact]
    public async Task DecideAsync_Default_AsksForWorkspaceWrite()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(prompt);

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
    }

    [Fact]
    public async Task DecideAsync_AutoEdit_AutoExecutesWorkspaceWrite()
    {
        using var context = new ApprovalContext(ApprovalMode.AutoEdit);
        var policy = context.CreatePolicy(new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
    }

    [Fact]
    public async Task DecideAsync_Full_AutoExecutesWorkspaceBash()
    {
        using var context = new ApprovalContext(ApprovalMode.Full);
        var policy = context.CreatePolicy(new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"dotnet test"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
    }

    [Fact]
    public async Task DecideAsync_Review_AllowsWriteWhenReviewerAllows()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var policy = context.CreatePolicy(
            new ThrowingApprovalPrompt(),
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("matches the request")));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
    }

    [Fact]
    public async Task DecideAsync_Review_RejectsWhenReviewerDenies()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var policy = context.CreatePolicy(
            new ThrowingApprovalPrompt(),
            new FixedApprovalReviewer(
                ApprovalReviewVerdict.Deny("not part of the requested work")));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(
            "The approval reviewer declined this action: not part of the requested work",
            decision.RejectionOutput?.Text);
    }

    [Fact]
    public async Task DecideAsync_Review_AsksUserWhenReviewerIsUncertain()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.AskUser("unclear")));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
    }

    [Fact]
    public async Task DecideAsync_Review_ForbiddenAllowStillAsksUser()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("looks fine")));

        var decision = await policy.DecideAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"sudo ls"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(Risk.Forbidden, prompt.LastClassification?.Risk);
    }

    [Fact]
    public async Task DecideAsync_Full_StillAsksForForbiddenBash()
    {
        using var context = new ApprovalContext(ApprovalMode.Full);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(prompt);

        var decision = await policy.DecideAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"sudo ls"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(Risk.Forbidden, prompt.LastClassification?.Risk);
    }

    [Fact]
    public async Task DecideAsync_Plan_RejectsWrite()
    {
        using var context = new ApprovalContext(ApprovalMode.Plan);
        var policy = context.CreatePolicy(new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal("Plan mode does not allow write.", decision.RejectionOutput?.Text);
    }

    [Fact]
    public async Task DecideAsync_Default_RejectsWhenUserDenies()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        var policy = context.CreatePolicy(new RecordingApprovalPrompt(ApprovalChoice.Deny));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(ApprovalPolicy.RejectedText, decision.RejectionOutput?.Text);
    }

    [Fact]
    public async Task DecideAsync_SessionGrant_SkipsLaterPrompt()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowSession);
        var policy = context.CreatePolicy(prompt);

        await policy.DecideAsync(WriteCall());
        var second = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, second.Action);
        Assert.Equal(1, prompt.Count);
    }

    private static ToolCall WriteCall() =>
        new("1", WriteTool.ToolName, """{"path":"src/App.cs","contents":"x"}""");

    private sealed class ApprovalContext : IDisposable
    {
        private readonly TemporaryWorkspace _workspace;
        private readonly TemporaryHome _home;
        private readonly ApprovalMode _mode;

        public ApprovalContext(ApprovalMode mode)
        {
            _mode = mode;
            _workspace = new TemporaryWorkspace();
            _home = new TemporaryHome();
        }

        public ApprovalPolicy CreatePolicy(
            IApprovalPrompt prompt,
            IApprovalReviewer? reviewer = null) =>
            new(
                _mode,
                new Workspace(_workspace.Path),
                new GrantStore(_home.Home),
                prompt,
                reviewer,
                new StaticApprovalReviewContext("Add a failing test."));

        public void Dispose()
        {
            _workspace.Dispose();
            _home.Dispose();
        }
    }
}
