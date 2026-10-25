using Crystal.Tools;

namespace CrystalCode.Engine.Approvals;

/// <summary>
/// One tool call plus the conversation the reviewer must check against.
/// An extended check may also carry the primary reviewer's verdict.
/// </summary>
public sealed record ApprovalReviewRequest(
    ToolCall Call,
    ToolClassification Classification,
    string Conversation,
    ApprovalReviewVerdict? PrimaryReview = null);
