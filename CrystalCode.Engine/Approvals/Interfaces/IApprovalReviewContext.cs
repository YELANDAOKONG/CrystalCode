using Crystal.Chat;

namespace CrystalCode.Engine.Approvals.Interfaces;

/// <summary>
/// Supplies conversation evidence to the approval reviewer.
/// </summary>
public interface IApprovalReviewContext
{
    IReadOnlyList<ChatItem> Conversation { get; }
}
