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
        ApprovalReviewVerdict? review = null, CancellationToken cancellationToken = default)
    {
        await operatorGate.WaitAsync(cancellationToken);
        try
        {
            return await inner.AskAsync(call, Label(classification), review, cancellationToken);
        }
        finally
        {
            operatorGate.Release();
        }
    }

    public void NotifyPassed(ToolCall call, ToolClassification classification, ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        var labeled = Label(classification);
        publish(new ApprovalPassed(call, labeled, reason, review));
        inner.NotifyPassed(call, labeled, reason, review);
    }

    public void NotifyReviewing(ToolCall call)
    {
        publish(new ApprovalReviewing(call));
        inner.NotifyReviewing(call);
    }
}
