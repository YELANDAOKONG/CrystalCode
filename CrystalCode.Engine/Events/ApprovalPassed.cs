using Crystal.Tools;

using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A classified tool call passed approval in its originating conversation.
/// <see cref="ExtendedReview"/> is set when the extended reviewer also allowed it.
/// </summary>
public sealed record ApprovalPassed(
    ToolCall Call,
    ToolClassification Classification,
    ApprovalPassReason Reason,
    ApprovalReviewVerdict? Review = null,
    ApprovalReviewVerdict? ExtendedReview = null) : SessionEvent;
