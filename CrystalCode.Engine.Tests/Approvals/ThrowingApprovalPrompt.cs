using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;

namespace CrystalCode.Engine.Tests.Approvals;

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
