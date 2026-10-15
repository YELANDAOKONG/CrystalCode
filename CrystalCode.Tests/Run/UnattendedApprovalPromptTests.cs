using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;
using CrystalCode.Run;

using Xunit;

namespace CrystalCode.Tests.Run;

public sealed class UnattendedApprovalPromptTests
{
    [Theory]
    [InlineData("review")]
    [InlineData("audit")]
    public async Task DecideAsync_ForcedPrompt_RejectsEvenWithGrantAndReviewerAllow(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("crystal-approval-");
        try
        {
            var workspace = new Workspace(directory.FullName);
            var grants = new GrantStore(new CrystalHome(Path.Combine(directory.FullName, "home")));
            var call = new ToolCall("1", "write", """{"path":"file.txt","contents":"Patch"}""");
            grants.Remember(workspace.Root, call, GrantScope.Persistent);
            var prompt = new UnattendedApprovalPrompt();
            var policy = new ApprovalPolicy(new ApprovalMode(mode), workspace, grants, prompt,
                new AllowingReviewer(), new StaticApprovalReviewContext("Update file.txt"),
                advise: static (_, classification) => classification with { RequirePrompt = true });

            var decision = await policy.DecideAsync(call);

            Assert.Equal(ToolInvocationAction.Reject, decision.Action);
            Assert.Equal(1, prompt.Asked);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class AllowingReviewer : IApprovalReviewer
    {
        public ValueTask<ApprovalReviewVerdict> ReviewAsync(
            ApprovalReviewRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ApprovalReviewVerdict.Allow("Requested update"));
    }
}
