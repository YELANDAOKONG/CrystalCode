using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional extended approval: a separate model that checks a call the
/// primary reviewer allowed in Review or Audit. It runs when the primary
/// risk level reaches <see cref="Trigger"/>. While disabled, a stored
/// selection, gear, slots, trigger, and primary-review switch are kept unused.
/// </summary>
public sealed record ExtendedApprovalSettings
{
    public const bool DefaultIncludePrimaryReview = true;

    public static ReviewRiskLevel DefaultTrigger => ReviewRiskLevel.Low;

    public static ExtendedApprovalSettings Off { get; } = new(ApprovalModelSettings.Off);

    public ExtendedApprovalSettings(
        ApprovalModelSettings reviewer,
        ReviewRiskLevel? trigger = null,
        bool includePrimaryReview = DefaultIncludePrimaryReview)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        Reviewer = reviewer;
        Trigger = trigger ?? DefaultTrigger;
        IncludePrimaryReview = includePrimaryReview;
    }

    /// <summary>
    /// The extended reviewer's switch, model, thinking gear, service tier, and speed.
    /// </summary>
    public ApprovalModelSettings Reviewer { get; }

    public bool Enabled => Reviewer.Enabled;

    /// <summary>
    /// Lowest primary-review risk level that runs the extended check.
    /// </summary>
    public ReviewRiskLevel Trigger { get; }

    /// <summary>
    /// Whether the extended reviewer sees the primary outcome and rationale.
    /// </summary>
    public bool IncludePrimaryReview { get; }

    public bool IsDefault =>
        !Reviewer.Enabled
        && !Reviewer.HasSelection
        && Reviewer.ThinkingEffort == ThinkingSelection.Default
        && Reviewer.ServiceTier is null
        && Reviewer.Speed is null
        && Trigger == DefaultTrigger
        && IncludePrimaryReview == DefaultIncludePrimaryReview;

    public ExtendedApprovalSettings WithReviewer(ApprovalModelSettings reviewer) =>
        new(reviewer, Trigger, IncludePrimaryReview);

    public ExtendedApprovalSettings WithTrigger(ReviewRiskLevel trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return new(Reviewer, trigger, IncludePrimaryReview);
    }

    public ExtendedApprovalSettings WithIncludePrimaryReview(bool includePrimaryReview) =>
        new(Reviewer, Trigger, includePrimaryReview);

    public string Describe() => Reviewer.Describe("Extended approval");
}
