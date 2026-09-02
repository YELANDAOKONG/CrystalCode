using CrystalCode.Approvals;

namespace CrystalCode.Prompts;

/// <summary>
/// Per-request values for approval-review user templates.
/// </summary>
public sealed record ReviewPromptContext(
    string Conversation,
    string ToolName,
    string ToolArguments,
    string HostRisk,
    string HostAuthority,
    string ClassificationSummary)
{
    public static ReviewPromptContext From(ApprovalReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ReviewPromptContext(
            request.Conversation.Trim(),
            request.Call.Name,
            request.Call.Arguments,
            request.Classification.Risk.Value,
            request.Classification.Authority.Value,
            request.Classification.Summary);
    }
}
