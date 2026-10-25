using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Events;

using Xunit;

namespace CrystalCode.Engine.Tests.Approvals;

public sealed class ChildApprovalPromptTests
{
    [Fact]
    public async Task Notifications_PublishLabeledEventsAndPreserveApprovalDecisions()
    {
        var inner = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var events = new List<SessionEvent>();
        using var gate = new SemaphoreSlim(1, 1);
        var prompt = new ChildApprovalPrompt(inner, () => "explore / research / child-one", events.Add, gate);
        var call = new ToolCall("read-one", "read", "{}", ToolInputKind.Json);
        var classification = new ToolClassification(Risk.Read, Authority.Workspace, "Read notes");

        prompt.NotifyReviewing(call, ApprovalReviewStage.Extended);
        prompt.NotifyPassed(call, classification, ApprovalPassReason.Review);
        var result = await prompt.AskAsync(call, classification);

        var reviewing = Assert.IsType<ApprovalReviewing>(events[0]);
        Assert.Same(call, reviewing.Call);
        Assert.Same(ApprovalReviewStage.Extended, reviewing.Stage);
        var passed = Assert.IsType<ApprovalPassed>(events[1]);
        Assert.Equal("Child explore / research / child-one: Read notes", passed.Classification.Summary);
        Assert.Same(ApprovalPassReason.Review, passed.Reason);
        Assert.Same(ApprovalChoice.Deny, result);
        Assert.Equal(1, inner.Count);
        Assert.Equal(1, inner.PassCount);
        Assert.Equal(1, inner.ReviewCount);
        Assert.Equal(passed.Classification, inner.LastClassification);
        Assert.Equal("Read notes", classification.Summary);
    }
}
