using Crystal.Tools;

namespace CrystalHarness.Approvals;

/// <summary>
/// One tool call plus the user request the reviewer must check against.
/// </summary>
public sealed record ApprovalReviewRequest(
    ToolCall Call,
    ToolClassification Classification,
    string UserRequest);
