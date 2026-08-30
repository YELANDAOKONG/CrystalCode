namespace CrystalHarness.Approvals;

/// <summary>
/// The reviewing model's decision for one tool call.
/// </summary>
public sealed record ApprovalReviewVerdict
{
    public static ApprovalReviewVerdict Allow(string reason) =>
        new("allow", reason);

    public static ApprovalReviewVerdict Deny(string reason) =>
        new("deny", reason);

    public static ApprovalReviewVerdict AskUser(string reason) =>
        new("ask", reason);

    private ApprovalReviewVerdict(string action, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Action = action;
        Reason = reason.Trim();
    }

    public string Action { get; }

    public string Reason { get; }

    public bool IsAllow => Action == "allow";

    public bool IsDeny => Action == "deny";

    public override string ToString() => $"{Action}: {Reason}";
}
