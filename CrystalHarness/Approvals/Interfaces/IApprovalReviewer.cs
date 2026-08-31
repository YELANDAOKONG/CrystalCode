namespace CrystalHarness.Approvals.Interfaces;

/// <summary>
/// Reviews a tool call for safety and whether it serves the user request.
/// </summary>
public interface IApprovalReviewer
{
    ValueTask<ApprovalReviewVerdict> ReviewAsync(
        ApprovalReviewRequest request,
        CancellationToken cancellationToken = default);
}
