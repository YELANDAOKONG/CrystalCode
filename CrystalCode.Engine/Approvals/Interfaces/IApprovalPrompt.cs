using Crystal.Tools;

namespace CrystalCode.Engine.Approvals.Interfaces;

/// <summary>
/// Asks the operator to approve or deny one classified tool call.
/// Auto-pass also reports through this surface so the shell can print it.
/// <c>extendedReview</c> is the extended reviewer's verdict when that check ran.
/// </summary>
public interface IApprovalPrompt
{
    ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null,
        CancellationToken cancellationToken = default);

    void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null);

    /// <summary>
    /// A review model is about to judge this call at <paramref name="stage"/>.
    /// </summary>
    void NotifyReviewing(ToolCall call, ApprovalReviewStage stage);
}
