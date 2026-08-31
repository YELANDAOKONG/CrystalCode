namespace CrystalHarness.Approvals.Interfaces;

/// <summary>
/// Supplies the current user request to the approval reviewer.
/// </summary>
public interface IApprovalReviewContext
{
    string CurrentUserRequest { get; }
}
