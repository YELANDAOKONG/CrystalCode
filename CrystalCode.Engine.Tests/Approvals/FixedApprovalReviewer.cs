using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;

namespace CrystalCode.Engine.Tests.Approvals;

internal sealed class FixedApprovalReviewer : IApprovalReviewer
{
    private readonly ApprovalReviewVerdict _verdict;

    public FixedApprovalReviewer(ApprovalReviewVerdict verdict)
    {
        _verdict = verdict;
    }

    public ValueTask<ApprovalReviewVerdict> ReviewAsync(
        ApprovalReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        return ValueTask.FromResult(_verdict);
    }

    public ApprovalReviewRequest? LastRequest { get; private set; }
}
