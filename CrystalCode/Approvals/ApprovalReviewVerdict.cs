namespace CrystalCode.Approvals;

/// <summary>
/// Reviewer assessment: outcome, residual risk, user authorization, rationale.
/// Field names follow Codex <c>GuardianAssessment</c>.
/// </summary>
public sealed record ApprovalReviewVerdict
{
    public ApprovalReviewVerdict(
        string outcome,
        ReviewRiskLevel riskLevel,
        ReviewAuthorization userAuthorization,
        string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentNullException.ThrowIfNull(riskLevel);
        ArgumentNullException.ThrowIfNull(userAuthorization);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);

        var normalized = outcome.Trim().ToLowerInvariant();
        if (normalized is not ("allow" or "deny" or "ask"))
        {
            throw new ArgumentException(
                "Outcome must be allow, deny, or ask.",
                nameof(outcome));
        }

        Outcome = normalized;
        RiskLevel = riskLevel;
        UserAuthorization = userAuthorization;
        Rationale = rationale.Trim();
    }

    public static ApprovalReviewVerdict Allow(string rationale) =>
        new("allow", ReviewRiskLevel.Low, ReviewAuthorization.High, rationale);

    public static ApprovalReviewVerdict Deny(string rationale) =>
        new("deny", ReviewRiskLevel.High, ReviewAuthorization.Low, rationale);

    public static ApprovalReviewVerdict AskUser(string rationale) =>
        new("ask", ReviewRiskLevel.Medium, ReviewAuthorization.Unknown, rationale);

    public string Outcome { get; }

    public ReviewRiskLevel RiskLevel { get; }

    public ReviewAuthorization UserAuthorization { get; }

    public string Rationale { get; }

    public bool IsAllow => Outcome == "allow";

    public bool IsDeny => Outcome == "deny";

    public override string ToString() =>
        $"{Outcome} risk={RiskLevel.Value} auth={UserAuthorization.Value}: {Rationale}";
}
