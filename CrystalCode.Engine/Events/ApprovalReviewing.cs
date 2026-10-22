using Crystal.Tools;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A tool call is being reviewed in its originating conversation.
/// </summary>
public sealed record ApprovalReviewing(ToolCall Call) : SessionEvent;
