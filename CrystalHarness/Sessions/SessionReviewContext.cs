using CrystalHarness.Approvals;
using CrystalHarness.Approvals.Interfaces;

namespace CrystalHarness.Sessions;

/// <summary>
/// Holds the latest user request for review-mode approval.
/// </summary>
public sealed class SessionReviewContext : IApprovalReviewContext
{
    public string CurrentUserRequest { get; set; } = string.Empty;
}
