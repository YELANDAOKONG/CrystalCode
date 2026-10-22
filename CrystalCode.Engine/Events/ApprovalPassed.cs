using Crystal.Tools;

using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A classified tool call passed approval in its originating conversation.
/// </summary>
public sealed record ApprovalPassed(
    ToolCall Call,
    ToolClassification Classification,
    ApprovalPassReason Reason,
    ApprovalReviewVerdict? Review = null) : SessionEvent;
