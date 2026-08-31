using Crystal.Chat;

using CrystalHarness.Approvals.Interfaces;

namespace CrystalHarness.Sessions;

/// <summary>
/// Holds the live conversation for review-mode approval.
/// </summary>
public sealed class SessionReviewContext : IApprovalReviewContext
{
    public IReadOnlyList<ChatItem> Conversation { get; set; } = [];
}
