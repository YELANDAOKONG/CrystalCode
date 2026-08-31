using CrystalHarness.Approvals.Interfaces;

namespace CrystalHarness.Approvals;

/// <summary>
/// Fixed user-request text for review-mode tests and early host wiring.
/// </summary>
public sealed class StaticApprovalReviewContext : IApprovalReviewContext
{
    public StaticApprovalReviewContext(string userRequest)
    {
        ArgumentNullException.ThrowIfNull(userRequest);
        CurrentUserRequest = userRequest;
    }

    public string CurrentUserRequest { get; }
}
