using Crystal.Chat;
using CrystalCode.Approvals.Interfaces;

namespace CrystalCode.Sessions;

/// <summary>
/// Holds the live conversation for review-mode approval.
/// </summary>
public sealed class SessionReviewContext : IApprovalReviewContext
{
    public IReadOnlyList<ChatItem> Conversation { get; set; } = [];
}
