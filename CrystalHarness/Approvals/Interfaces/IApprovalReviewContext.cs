using Crystal.Chat;

namespace CrystalHarness.Approvals.Interfaces;

/// <summary>
/// Supplies conversation evidence to the approval reviewer.
/// </summary>
public interface IApprovalReviewContext
{
    IReadOnlyList<ChatItem> Conversation { get; }
}
