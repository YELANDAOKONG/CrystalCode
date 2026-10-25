using CrystalCode.Engine.Approvals.Interfaces;

namespace CrystalCode.Engine.Approvals;

/// <summary>
/// The extended reviewer and when it runs: after a primary allow whose risk
/// level reaches <see cref="Trigger"/>. <see cref="IncludePrimaryReview"/>
/// decides whether the primary verdict is attached to the request.
/// </summary>
public sealed record ExtendedApprovalReviewer
{
    public ExtendedApprovalReviewer(
        IApprovalReviewer reviewer,
        ReviewRiskLevel trigger,
        bool includePrimaryReview)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        ArgumentNullException.ThrowIfNull(trigger);
        Reviewer = reviewer;
        Trigger = trigger;
        IncludePrimaryReview = includePrimaryReview;
    }

    public IApprovalReviewer Reviewer { get; }

    public ReviewRiskLevel Trigger { get; }

    public bool IncludePrimaryReview { get; }

    public bool Applies(ApprovalReviewVerdict primary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        return primary.RiskLevel.IsAtLeast(Trigger);
    }
}
