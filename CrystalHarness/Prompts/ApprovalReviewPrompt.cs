using CrystalHarness.Approvals;

namespace CrystalHarness.Prompts;

/// <summary>
/// Caller-authored text for the approval-review model.
/// Shape follows Codex guardian review: user request first, then the action,
/// then a strict assessment JSON object.
/// </summary>
public static class ApprovalReviewPrompt
{
    public const string SystemText =
        """
        You are a separate approval reviewer. You do not execute the action.
        First assign residual risk and how strongly the user request authorizes
        this exact action. Then set outcome from those two judgments.

        - allow: the action is safe enough and the user request authorizes it
        - deny: unsafe, destructive, or not authorized by the user request
        - ask: you cannot decide without the operator

        The user request is required. Judge authorization against that request
        only. If the request is missing, reply with outcome ask.

        Reply with a single JSON object and no other text:
        {"outcome":"allow"|"deny"|"ask","risk_level":"low"|"medium"|"high","user_authorization":"low"|"medium"|"high","rationale":"short English explanation"}
        """;

    public static string UserText(ApprovalReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            throw new ArgumentException(
                "The user request must be attached for approval review.",
                nameof(request));
        }

        return
            $"""
            ## User request
            {request.UserRequest.Trim()}

            ## Proposed action
            Tool: {request.Call.Name}
            Host risk: {request.Classification.Risk.Value}
            Host authority: {request.Classification.Authority.Value}
            Summary: {request.Classification.Summary}
            Arguments:
            {request.Call.Arguments}

            Assess this exact action against the user request above.
            """;
    }
}
