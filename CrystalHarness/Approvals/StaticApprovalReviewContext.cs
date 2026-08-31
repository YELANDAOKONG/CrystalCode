using Crystal.Chat;

using CrystalHarness.Approvals.Interfaces;

namespace CrystalHarness.Approvals;

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
