using Crystal.Tools;

namespace CrystalCode.Approvals;

/// <summary>
/// One tool call plus the conversation the reviewer must check against.
/// </summary>
public sealed record ApprovalReviewRequest(
    ToolCall Call,
    ToolClassification Classification,
    string Conversation);
