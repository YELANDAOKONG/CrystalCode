namespace CrystalCode.Approvals.Interfaces;

/// <summary>
/// Reviews a tool call for safety and whether it serves the user's task.
/// </summary>
public interface IApprovalReviewer
{
    ValueTask<ApprovalReviewVerdict> ReviewAsync(
        ApprovalReviewRequest request,
        CancellationToken cancellationToken = default);
}
