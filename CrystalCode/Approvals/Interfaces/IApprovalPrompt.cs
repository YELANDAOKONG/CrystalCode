using Crystal.Tools;

namespace CrystalCode.Approvals.Interfaces;

/// <summary>
/// Asks the operator to approve or deny one classified tool call.
/// Auto-pass also reports through this surface so the shell can print it.
/// </summary>
public interface IApprovalPrompt
{
    ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default);

    void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null);
}
