using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class DirectoryAccessPolicyTests
{
    [Fact]
    public void ModeFor_MostSpecificRuleWinsAndWriteIncludesRead()
    {
        using var root = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        var parent = Path.GetDirectoryName(workspace.Root)!;
        workspace.Access.Set(parent, "read");
        Assert.Equal("write", workspace.Access.ModeFor(workspace.Root, workspace.Root));
        var child = Path.Combine(workspace.Root, "vendor");
        workspace.Access.Set(child, "read");
        Assert.True(workspace.IsReadOnly(Path.Combine(child, "a.txt")));
        Assert.False(workspace.IsReadOnly(child + "2"));
        workspace.Access.Set(Path.Combine(child, "generated"), "write");
        Assert.False(workspace.IsReadOnly(Path.Combine(child, "generated", "a.txt")));
        Assert.True(workspace.IsAccessible(Path.Combine(child, "a.txt")));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("edit")]
    [InlineData("review")]
    [InlineData("audit")]
    [InlineData("full")]
    public async Task DecideAsync_ReadOnlyWriteCannotUseReviewGrantOrRawAdvice(string mode)
    {
        using var root = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var workspace = new Workspace(root.Path);
        var file = Path.Combine(workspace.Root, "new", "a.txt");
        workspace.Access.Set(workspace.Root, "read");
        var call = JsonToolCall("1", "write", JsonSerializer.Serialize(new { path = file, contents = "new" }));
        var grants = new GrantStore(home.Home);
        grants.Remember(workspace.Root, call, GrantScope.Session);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.AllowOnce);
        var policy = new ApprovalPolicy(ApprovalMode.Parse(mode), workspace, grants, prompt,
            new FixedApprovalReviewer(ApprovalReviewVerdict.Allow("allowed")),
            advise: (_, _) => new ToolClassification(Risk.Read, Authority.Workspace, "Allowed"));

        var decision = await policy.DecideAsync(call);
        var result = await new WriteTool(workspace).InvokeAsync(call);

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
        Assert.Equal(0, prompt.Count);
        Assert.Equal(0, prompt.ReviewCount);
    }

    [Theory]
    [InlineData("read", "read", "default", true)]
    [InlineData("write", "read", "default", true)]
    [InlineData("write", "write", "edit", true)]
    [InlineData("write", "write", "default", false)]
    [InlineData("write", "write", "audit", false)]
    [InlineData("write", "write", "full", true)]
    [InlineData("read", "write", "full", false)]
    public async Task DecideAsync_AdditionalDirectoryFollowsMode(string access, string tool, string mode, bool executes)
    {
        using var root = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var workspace = new Workspace(root.Path);
        workspace.Access.Set(new Workspace(other.Path).Root, access);
        var prompt = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var policy = new ApprovalPolicy(ApprovalMode.Parse(mode), workspace, new GrantStore(home.Home), prompt);
        var call = JsonToolCall("1", tool, JsonSerializer.Serialize(new
        {
            path = Path.Combine(other.Path, "a.txt"), contents = "new"
        }));

        var result = await policy.DecideAsync(call);

        Assert.Equal(executes ? ToolInvocationAction.Execute : ToolInvocationAction.Reject, result.Action);
    }

    [Fact]
    public async Task InvokeAsync_EditChecksReadOnlyAndPreservesContents()
    {
        using var root = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        workspace.Access.Set(workspace.Root, "read");
        var file = Path.Combine(root.Path, "a.txt");
        await File.WriteAllTextAsync(file, "before");
        var call = JsonToolCall("1", "edit", JsonSerializer.Serialize(new
        {
            path = file, old_string = "before", new_string = "after"
        }));

        var result = await new EditTool(workspace).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Equal("before", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public void TryResolveOutputPath_SymlinkTargetUsesItsOwnRule()
    {
        using var root = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        workspace.Access.Set(new Workspace(other.Path).Root, "read");
        var link = Path.Combine(root.Path, "reference");
        Directory.CreateSymbolicLink(link, other.Path);

        Assert.False(workspace.TryResolveOutputPath(Path.Combine(link, "new", "a.txt"), out _, out var error));
        Assert.Contains("read-only", error, StringComparison.Ordinal);
        Assert.False(workspace.TryResolveOutputPath(Path.Combine(link, "new.txt"), out _, out _));
    }
}
