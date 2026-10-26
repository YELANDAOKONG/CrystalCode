using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Notifications;

/// <summary>
/// Sends the approval notification while the operator is asked. Auto-pass and
/// review notices are not operator waits and pass through untouched.
/// </summary>
internal sealed class NotifyingApprovalPrompt(IApprovalPrompt inner, OperatorNotifier notifier) : IApprovalPrompt
{
    public async ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        using var pending = notifier.Begin(new OperatorNotice(
            OperatorNotifyEvent.Approval,
            "Approval needed: " + classification.Summary,
            [
                new("TOOL", call.Name),
                new("SUMMARY", classification.Summary),
                new("RISK", classification.Risk.Value),
                new("AUTHORITY", classification.Authority.Value)
            ]));
        return await inner.AskAsync(call, classification, review, extendedReview, cancellationToken);
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null) =>
        inner.NotifyPassed(call, classification, reason, review, extendedReview);

    public void NotifyReviewing(ToolCall call, ApprovalReviewStage stage) =>
        inner.NotifyReviewing(call, stage);
}
