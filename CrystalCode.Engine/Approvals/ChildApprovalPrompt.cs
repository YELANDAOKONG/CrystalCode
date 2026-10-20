using Crystal.Tools;

using CrystalCode.Engine.Approvals.Interfaces;

namespace CrystalCode.Engine.Approvals;

internal sealed class ChildApprovalPrompt(IApprovalPrompt inner, Func<string?> source) : IApprovalPrompt
{
    private ToolClassification Label(ToolClassification classification) => source() is string label
        ? classification with { Summary = $"Child {label}: {classification.Summary}" }
        : classification;

    public ValueTask<ApprovalChoice> AskAsync(ToolCall call, ToolClassification classification,
        ApprovalReviewVerdict? review = null, CancellationToken cancellationToken = default) =>
        inner.AskAsync(call, Label(classification), review, cancellationToken);

    public void NotifyPassed(ToolCall call, ToolClassification classification, ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null) => inner.NotifyPassed(call, Label(classification), reason, review);

    public void NotifyReviewing(ToolCall call) => inner.NotifyReviewing(call);
}
