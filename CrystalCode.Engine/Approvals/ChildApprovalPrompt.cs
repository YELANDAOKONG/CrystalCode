using Crystal.Tools;

using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Approvals;

/// <summary>
/// Labels child approvals and holds the operator gate only while the operator is asked,
/// so classification and review never block other operator overlays such as the child picker.
/// </summary>
internal sealed class ChildApprovalPrompt(
    IApprovalPrompt inner,
    Func<string?> source,
    Action<SessionEvent> publish,
    SemaphoreSlim operatorGate) : IApprovalPrompt
{
    private ToolClassification Label(ToolClassification classification) => source() is string label
        ? classification with { Summary = $"Child {label}: {classification.Summary}" }
        : classification;

    public async ValueTask<ApprovalChoice> AskAsync(ToolCall call, ToolClassification classification,
        ApprovalReviewVerdict? review = null, ApprovalReviewVerdict? extendedReview = null,
        CancellationToken cancellationToken = default)
    {
        await operatorGate.WaitAsync(cancellationToken);
        try
        {
            return await inner.AskAsync(call, Label(classification), review, extendedReview, cancellationToken);
        }
        finally
        {
            operatorGate.Release();
        }
    }

    public void NotifyPassed(ToolCall call, ToolClassification classification, ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null, ApprovalReviewVerdict? extendedReview = null)
    {
        var labeled = Label(classification);
        publish(new ApprovalPassed(call, labeled, reason, review, extendedReview));
        inner.NotifyPassed(call, labeled, reason, review, extendedReview);
    }

    public void NotifyReviewing(ToolCall call, ApprovalReviewStage stage)
    {
        publish(new ApprovalReviewing(call, stage));
        inner.NotifyReviewing(call, stage);
    }
}
