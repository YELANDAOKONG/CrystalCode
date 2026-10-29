namespace CrystalCode.Engine.Events;

/// <summary>
/// Enabled extended approval as the status line shows it: its reviewer, the
/// lowest primary risk level that triggers it, and whether it sees the
/// primary review.
/// </summary>
public sealed record ExtendedApprovalChrome(
    ReviewerChrome Reviewer,
    string Trigger,
    bool IncludePrimaryReview);
