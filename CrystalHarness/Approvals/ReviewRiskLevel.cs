namespace CrystalHarness.Approvals;

/// <summary>
/// Residual risk assigned by the approval reviewer.
/// Aligns with Codex <c>GuardianRiskLevel</c> low / medium / high.
/// </summary>
public sealed record ReviewRiskLevel
{
    public static ReviewRiskLevel Low { get; } = new("low");

    public static ReviewRiskLevel Medium { get; } = new("medium");

    public static ReviewRiskLevel High { get; } = new("high");

    public ReviewRiskLevel(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = Normalize(value);
    }

    public string Value { get; }

    public static bool TryParse(string? value, out ReviewRiskLevel level)
    {
        level = Medium;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        if (normalized is not ("low" or "medium" or "high"))
        {
            return false;
        }

        level = new ReviewRiskLevel(normalized);
        return true;
    }

    public override string ToString() => Value;

    private static string Normalize(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed == "critical" ? High.Value : trimmed;
    }
}
