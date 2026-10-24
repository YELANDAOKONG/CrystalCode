using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Approvals;

public sealed class ApplyPatchApprovalTests
{
    [Fact]
    public void Classify_PatchInsideWorkspace_IsWorkspaceWrite()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));

        var classification = classifier.Classify(Call("*** Update File: a.txt\n@@\n-a\n+b", "*** Add File: b/c.txt\n+x"));

        Assert.Equal(Risk.Write, classification.Risk);
        Assert.Equal(Authority.Workspace, classification.Authority);
        Assert.True(classification.AllowsReusableGrant);
    }

    [Fact]
    public void Classify_OneOutsidePath_MakesTheWholePatchOutside()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));
        var target = Path.Combine(outside.Path, "note.txt").Replace('\\', '/');

        var classification = classifier.Classify(
            Call("*** Add File: inside.txt\n+x", $"*** Add File: {target}\n+y"));

        Assert.Equal(Risk.Write, classification.Risk);
        Assert.Equal(Authority.OutsideWorkspace, classification.Authority);
        Assert.False(classification.AllowsReusableGrant);
    }

    [Fact]
    public void Classify_OneCredentialPath_MakesTheWholePatchForbidden()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));

        var classification = classifier.Classify(
            Call("*** Add File: fine.txt\n+x", "*** Delete File: .ssh/id_rsa"));

        Assert.Equal(Risk.Forbidden, classification.Risk);
        Assert.Equal(Authority.PrivilegedEscalation, classification.Authority);
    }

    [Fact]
    public void Classify_MoveDestinationIsChecked()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));

        var classification = classifier.Classify(
            Call("*** Update File: a.txt\n*** Move to: .ssh/authorized_keys\n@@\n-a\n+b"));

        Assert.Equal(Risk.Forbidden, classification.Risk);
    }

    [Fact]
    public void Classify_UnreadablePatch_StaysAWorkspaceWriteSoTheToolReportsTheError()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));

        var classification = classifier.Classify(
            JsonToolCall("1", ApplyPatchTool.ToolName, """{"patchText":"nonsense"}"""));

        Assert.Equal(Risk.Write, classification.Risk);
        Assert.Equal(Authority.Workspace, classification.Authority);
    }

    [Fact]
    public void GrantFingerprint_IsTheSortedPathSetNotThePatchBody()
    {
        var first = GrantFingerprint.Create(Call("*** Update File: b.txt\n@@\n-1\n+2", "*** Add File: a.txt\n+x"));
        var second = GrantFingerprint.Create(Call("*** Add File: a.txt\n+different", "*** Update File: b.txt\n@@\n-3\n+4"));
        var other = GrantFingerprint.Create(Call("*** Add File: c.txt\n+x"));

        Assert.Equal("a.txt\nb.txt", first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("edit")]
    [InlineData("review")]
    [InlineData("audit")]
    public async Task DecideAsync_PatchTouchingAReadOnlyDirectoryIsRejected(string mode)
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        using var readOnly = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        workspace.Access.Set(new Workspace(readOnly.Path).Root, "read");
        var locked = Path.Combine(new Workspace(readOnly.Path).Root, "a.txt").Replace('\\', '/');
        var call = Call("*** Add File: fine.txt\n+x", $"*** Add File: {locked}\n+y");
        var grants = new GrantStore(home.Home);
        grants.Remember(workspace.Root, call, GrantScope.Session);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = new ApprovalPolicy(
            ApprovalMode.Parse(mode),
            workspace,
            grants,
            prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("allowed")),
            advise: (_, _) => new ToolClassification(Risk.Read, Authority.Workspace, "Allowed"));

        var decision = await policy.DecideAsync(call);

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(0, prompt.Count);
        Assert.Equal(0, prompt.ReviewCount);
    }

    [Fact]
    public async Task DecideAsync_PlanRejectsPatch()
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var policy = new ApprovalPolicy(
            ApprovalMode.Plan,
            new Workspace(root.Path),
            new GrantStore(home.Home),
            new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(Call("*** Add File: a.txt\n+x"));

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal("Plan mode does not allow apply_patch.", decision.RejectionOutput?.Text);
    }

    [Theory]
    [InlineData("edit", true)]
    [InlineData("review", true)]
    [InlineData("default", false)]
    public async Task DecideAsync_WorkspacePatchPassesInEditAndReviewOnly(string mode, bool passes)
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = new ApprovalPolicy(
            ApprovalMode.Parse(mode),
            new Workspace(root.Path),
            new GrantStore(home.Home),
            prompt);

        var decision = await policy.DecideAsync(Call("*** Add File: a.txt\n+x"));

        Assert.Equal(passes ? ToolInvocationAction.Execute : ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(passes ? 0 : 1, prompt.Count);
    }

    [Fact]
    public async Task DecideAsync_ForbiddenPatchNeverAutoPasses()
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = new ApprovalPolicy(
            ApprovalMode.Edit,
            new Workspace(root.Path),
            new GrantStore(home.Home),
            prompt);

        var decision = await policy.DecideAsync(Call("*** Add File: ok.txt\n+x", "*** Add File: .gnupg/key\n+y"));

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(1, prompt.Count);
        Assert.Equal(Risk.Forbidden, prompt.LastClassification?.Risk);
    }

    [Fact]
    public void ReservedNames_IncludeApplyPatch()
    {
        Assert.True(CrystalCode.Engine.Tools.External.ExternalToolNames.IsReserved(ApplyPatchTool.ToolName));
        Assert.Contains(ApplyPatchTool.ToolName, BuiltInToolSelection.Names);
    }

    private static ToolCall Call(params string[] sections)
    {
        var patch = "*** Begin Patch\n" + string.Join('\n', sections) + "\n*** End Patch";
        return JsonToolCall(
            "1",
            ApplyPatchTool.ToolName,
            JsonSerializer.Serialize(new { patchText = patch }));
    }
}
