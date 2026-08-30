using Crystal.Tools;

namespace CrystalHarness.Approvals;

/// <summary>
/// Asks the operator to approve or deny one classified tool call.
/// </summary>
public interface IApprovalPrompt
{
    ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default);
}
