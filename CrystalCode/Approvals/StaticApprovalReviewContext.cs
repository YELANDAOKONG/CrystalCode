using Crystal.Chat;
using CrystalCode.Approvals.Interfaces;

namespace CrystalCode.Approvals;

/// <summary>
/// Fixed conversation items for review-mode tests and early host wiring.
/// </summary>
public sealed class StaticApprovalReviewContext : IApprovalReviewContext
{
    public StaticApprovalReviewContext(string userRequest)
        : this(CreateConversation(userRequest))
    {
    }

    public StaticApprovalReviewContext(IReadOnlyList<ChatItem> conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        Conversation = conversation;
    }

    public IReadOnlyList<ChatItem> Conversation { get; }

    private static IReadOnlyList<ChatItem> CreateConversation(string userRequest)
    {
        ArgumentNullException.ThrowIfNull(userRequest);
        if (string.IsNullOrWhiteSpace(userRequest))
        {
            return [];
        }

        return [new ChatMessage(ChatRole.User, userRequest)];
    }
}
