using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Approvals;
using CrystalCode.Approvals.Interfaces;
using CrystalCode.Skills;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Approvals;

public sealed class ApprovalPolicyTests
{
    [Fact]
    public async Task DecideAsync_Default_AutoExecutesRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(prompt);

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, """{"path":"a.txt"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
        Assert.Equal(Risk.Read, prompt.LastClassification?.Risk);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Default_AsksForOutsideRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        using var outside = new TemporaryWorkspace();
        var file = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(file, "hello");
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(prompt);
        var json = "{\"path\":\"" + file.Replace("\\", "/") + "\"}";

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(0, prompt.PassCount);
        Assert.Equal(Risk.Read, prompt.LastClassification?.Risk);
        Assert.Equal(Authority.OutsideWorkspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Review_AllowsOutsideReadWhenReviewerAllows()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        using var outside = new TemporaryWorkspace();
        var file = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(file, "hello");
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var reviewer = new FixedApprovalReviewer(
            ApprovalReviewVerdict.Allow("outside read matches the task"));
        var policy = context.CreatePolicy(prompt, reviewer);
        var json = "{\"path\":\"" + file.Replace("\\", "/") + "\"}";

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(1, prompt.ReviewCount);
        Assert.Equal(ApprovalPassReason.Review, prompt.LastPassReason);
        Assert.NotNull(reviewer.LastRequest);
        Assert.Equal(Authority.OutsideWorkspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Full_StillAsksForOutsideRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Full);
        using var outside = new TemporaryWorkspace();
        var file = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(file, "hello");
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(prompt);
        var json = "{\"path\":\"" + file.Replace("\\", "/") + "\"}";

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(Authority.OutsideWorkspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Plan_AsksForOutsideRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Plan);
        using var outside = new TemporaryWorkspace();
        var file = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(file, "hello");
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(prompt);
        var json = "{\"path\":\"" + file.Replace("\\", "/") + "\"}";

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(Authority.OutsideWorkspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Default_AutoExecutesSkillsDirectoryRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Default);
        using var outside = new TemporaryWorkspace();
        var skillsRoot = Path.Combine(outside.Path, "skills");
        Directory.CreateDirectory(skillsRoot);
        var loose = Path.Combine(skillsRoot, "notes.md");
        File.WriteAllText(loose, "extra");
        var catalog = new SkillCatalog([], [skillsRoot]);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(prompt, skills: catalog);
        var json = "{\"path\":\"" + loose.Replace("\\", "/") + "\"}";

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Review_AutoExecutesSkillsDirectoryRead()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        using var outside = new TemporaryWorkspace();
        var skillsRoot = Path.Combine(outside.Path, "skills");
        Directory.CreateDirectory(skillsRoot);
        var nested = Path.Combine(skillsRoot, "demo-skill", "scripts", "setup.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "echo");
        var catalog = new SkillCatalog([], [skillsRoot]);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Deny("should not run")),
            skills: catalog);
        var json = "{\"path\":\"" + nested.Replace("\\", "/") + "\"}";

        var decision = await policy.DecideAsync(
            new ToolCall("1", ReadTool.ToolName, json));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
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
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(prompt);

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
        Assert.Equal(Risk.Write, prompt.LastClassification?.Risk);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Full_AutoExecutesWorkspaceBash()
    {
        using var context = new ApprovalContext(ApprovalMode.Full);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(prompt);

        var decision = await policy.DecideAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"dotnet test"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
        Assert.Equal(Risk.Write, prompt.LastClassification?.Risk);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
    }

    [Fact]
    public async Task DecideAsync_Review_AutoExecutesWorkspaceWrite()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var reviewer = new FixedApprovalReviewer(
            ApprovalReviewVerdict.Deny("should not run"));
        var policy = context.CreatePolicy(prompt, reviewer);

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(0, prompt.ReviewCount);
        Assert.Equal(ApprovalPassReason.Policy, prompt.LastPassReason);
        Assert.Equal(Risk.Write, prompt.LastClassification?.Risk);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
        Assert.Null(reviewer.LastRequest);
    }

    [Fact]
    public async Task DecideAsync_Review_AllowsBashWhenReviewerAllows()
    {
        using var context = new ApprovalContext(ApprovalMode.Review);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("matches the request")));

        var decision = await policy.DecideAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"dotnet test"}"""));

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(1, prompt.ReviewCount);
        Assert.Equal(ApprovalPassReason.Review, prompt.LastPassReason);
        Assert.Equal(Risk.Write, prompt.LastClassification?.Risk);
        Assert.NotNull(prompt.LastReview);
        Assert.Equal("matches the request", prompt.LastReview.Rationale);
    }

    [Fact]
    public async Task DecideAsync_FullReview_AllowsWriteWhenReviewerAllows()
    {
        using var context = new ApprovalContext(ApprovalMode.FullReview);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("matches the request")));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(1, prompt.ReviewCount);
        Assert.Equal(ApprovalPassReason.Review, prompt.LastPassReason);
        Assert.Equal(Risk.Write, prompt.LastClassification?.Risk);
        Assert.NotNull(prompt.LastReview);
        Assert.Equal(ReviewRiskLevel.Low, prompt.LastReview.RiskLevel);
        Assert.Equal(ReviewAuthorization.High, prompt.LastReview.UserAuthorization);
        Assert.Equal("matches the request", prompt.LastReview.Rationale);
    }

    [Fact]
    public async Task DecideAsync_FullReview_RejectsWhenReviewerDenies()
    {
        using var context = new ApprovalContext(ApprovalMode.FullReview);
        var policy = context.CreatePolicy(
            new ThrowingApprovalPrompt(),
            new FixedApprovalReviewer(
                ApprovalReviewVerdict.Deny("not part of the requested work")));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(
            "The approval reviewer declined this action: not part of the requested work"
            + ApprovalPolicy.RetryGuidance,
            decision.RejectionOutput?.Text);
    }

    [Fact]
    public async Task DecideAsync_FullReview_AsksUserWhenReviewerIsUncertain()
    {
        using var context = new ApprovalContext(ApprovalMode.FullReview);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.AskUser("unclear")));

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.NotNull(prompt.LastReview);
    }

    [Fact]
    public async Task DecideAsync_FullReview_AsksUserWhenConversationMissing()
    {
        using var context = new ApprovalContext(ApprovalMode.FullReview);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = context.CreatePolicy(
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("should not run")),
            userRequest: "");

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(1, prompt.Count);
    }

    [Fact]
    public async Task DecideAsync_FullReview_AttachesEarlierUserTurns()
    {
        using var context = new ApprovalContext(ApprovalMode.FullReview);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var reviewer = new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("matches the task"));
        var policy = context.CreatePolicy(
            prompt,
            reviewer,
            conversation:
            [
                new ChatMessage(ChatRole.System, "You are Crystal Code."),
                new ChatMessage(ChatRole.User, "Run the tests and fix failures."),
                new ChatMessage(ChatRole.Assistant, "Working."),
                new ChatMessage(ChatRole.User, "how's it going?")
            ]);

        var decision = await policy.DecideAsync(WriteCall());

        Assert.Equal(ToolInvocationAction.Execute, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.NotNull(reviewer.LastRequest);
        Assert.Contains(
            "Run the tests and fix failures.",
            reviewer.LastRequest.Conversation,
            StringComparison.Ordinal);
        Assert.Contains(
            "how's it going?",
            reviewer.LastRequest.Conversation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "You are Crystal Code.",
            reviewer.LastRequest.Conversation,
            StringComparison.Ordinal);
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
        Assert.Equal(1, prompt.PassCount);
        Assert.Equal(ApprovalPassReason.Grant, prompt.LastPassReason);
        Assert.Equal(Risk.Write, prompt.LastClassification?.Risk);
        Assert.Equal(Authority.Workspace, prompt.LastClassification?.Authority);
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
            IApprovalReviewer? reviewer = null,
            string userRequest = "Add a failing test.",
            IReadOnlyList<ChatItem>? conversation = null,
            SkillCatalog? skills = null) =>
            new(
                _mode,
                new Workspace(_workspace.Path),
                new GrantStore(_home.Home),
                prompt,
                reviewer,
                conversation is null
                    ? new StaticApprovalReviewContext(userRequest)
                    : new StaticApprovalReviewContext(conversation),
                skills: skills);

        public void Dispose()
        {
            _workspace.Dispose();
            _home.Dispose();
        }
    }
}
