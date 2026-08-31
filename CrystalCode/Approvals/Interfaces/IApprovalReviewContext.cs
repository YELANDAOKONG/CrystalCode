using Crystal.Chat;

namespace CrystalCode.Approvals.Interfaces;

/// <summary>
/// Supplies conversation evidence to the approval reviewer.
/// </summary>
public interface IApprovalReviewContext
{
    IReadOnlyList<ChatItem> Conversation { get; }
}
