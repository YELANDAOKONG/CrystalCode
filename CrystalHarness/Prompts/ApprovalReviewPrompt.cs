using CrystalHarness.Approvals;

namespace CrystalHarness.Prompts;

/// <summary>
/// Caller-authored text for the approval-review model.
/// </summary>
public static class ApprovalReviewPrompt
{
    public const string SystemText =
        """
        You review one coding-agent tool call before it runs.
        Allow it only when it is safe and it serves the user's request.
        Deny it when it is unsafe, destructive, or unrelated to the request.
        Ask the operator when you are uncertain.

        Reply with a single JSON object and no other text:
        {"decision":"allow"|"deny"|"ask","reason":"short English explanation"}
        """;

    public static string UserText(ApprovalReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userRequest = string.IsNullOrWhiteSpace(request.UserRequest)
            ? "(no user request was provided)"
            : request.UserRequest.Trim();
        return
            $"""
            User request:
            {userRequest}

            Tool: {request.Call.Name}
            Risk: {request.Classification.Risk.Value}
            Authority: {request.Classification.Authority.Value}
            Summary: {request.Classification.Summary}
            Arguments:
            {request.Call.Arguments}
            """;
    }
}
