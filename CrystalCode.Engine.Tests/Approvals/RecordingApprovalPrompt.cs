using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;

namespace CrystalCode.Engine.Tests.Approvals;

internal sealed class RecordingApprovalPrompt : IApprovalPrompt
{
    private readonly ApprovalChoice _choice;

    public RecordingApprovalPrompt(ApprovalChoice choice)
    {
        _choice = choice;
    }

    public int Count { get; private set; }

    public int PassCount { get; private set; }

    public int ReviewCount { get; private set; }

    public List<ApprovalReviewStage> ReviewStages { get; } = [];

    public ApprovalPassReason? LastPassReason { get; private set; }

    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count++;
        LastClassification = classification;
        LastReview = review;
        LastExtendedReview = extendedReview;
        return ValueTask.FromResult(_choice);
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        PassCount++;
        LastPassReason = reason;
        LastClassification = classification;
        LastReview = review;
        LastExtendedReview = extendedReview;
    }

    public void NotifyReviewing(ToolCall call, ApprovalReviewStage stage)
    {
        ArgumentNullException.ThrowIfNull(call);
        ReviewCount++;
        ReviewStages.Add(stage);
    }

    public ToolClassification? LastClassification { get; private set; }

    public ApprovalReviewVerdict? LastReview { get; private set; }

    public ApprovalReviewVerdict? LastExtendedReview { get; private set; }
}
