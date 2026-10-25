using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Per-request values for approval-review user templates.
/// <see cref="PrimaryReview"/> is empty except on an extended check that
/// includes the primary verdict.
/// </summary>
public sealed record ReviewPromptContext(
    string Conversation,
    string ToolName,
    string ToolArguments,
    string HostRisk,
    string HostAuthority,
    string ClassificationSummary,
    string PrimaryReview = "")
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
            request.Classification.Summary,
            RenderPrimary(request.PrimaryReview));
    }

    private static string RenderPrimary(ApprovalReviewVerdict? review)
    {
        if (review is null)
        {
            return string.Empty;
        }

        return "Outcome: " + review.Outcome
            + "\nRisk level: " + review.RiskLevel.Value
            + "\nUser authorization: " + review.UserAuthorization.Value
            + "\nRationale: " + review.Rationale;
    }
}
