using Crystal.Tools;

using CrystalCode.Approvals;
using CrystalCode.Approvals.Interfaces;

namespace CrystalCode.Tests.Approvals;

internal sealed class ThrowingApprovalPrompt : IApprovalPrompt
{
    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Approval prompt should not be called.");

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
    }

    public void NotifyReviewing(ToolCall call)
    {
    }
}
