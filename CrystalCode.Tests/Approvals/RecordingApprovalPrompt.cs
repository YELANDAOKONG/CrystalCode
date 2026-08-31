using Crystal.Tools;

using CrystalCode.Approvals;
using CrystalCode.Approvals.Interfaces;

namespace CrystalCode.Tests.Approvals;

internal sealed class RecordingApprovalPrompt : IApprovalPrompt
{
    private readonly ApprovalChoice _choice;

    public RecordingApprovalPrompt(ApprovalChoice choice)
    {
        _choice = choice;
    }

    public int Count { get; private set; }

    public int PassCount { get; private set; }

    public ApprovalPassReason? LastPassReason { get; private set; }

    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count++;
        LastClassification = classification;
        LastReview = review;
        return ValueTask.FromResult(_choice);
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        PassCount++;
        LastPassReason = reason;
        LastClassification = classification;
        LastReview = review;
    }

    public ToolClassification? LastClassification { get; private set; }

    public ApprovalReviewVerdict? LastReview { get; private set; }
}
