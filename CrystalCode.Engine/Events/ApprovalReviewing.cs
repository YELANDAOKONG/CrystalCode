using Crystal.Tools;

using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A tool call is being reviewed in its originating conversation, by the
/// primary reviewer or by the extended reviewer.
/// </summary>
public sealed record ApprovalReviewing(ToolCall Call, ApprovalReviewStage Stage) : SessionEvent;
