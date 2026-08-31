namespace CrystalCode.Approvals;

/// <summary>
/// How strongly the user's authorized task permits the reviewed action.
/// Mirrors Codex <c>GuardianUserAuthorization</c>.
/// </summary>
public sealed record ReviewAuthorization
{
    public static ReviewAuthorization Unknown { get; } = new("unknown");

    public static ReviewAuthorization Low { get; } = new("low");

    public static ReviewAuthorization Medium { get; } = new("medium");

    public static ReviewAuthorization High { get; } = new("high");

    public ReviewAuthorization(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static bool TryParse(string? value, out ReviewAuthorization authorization)
    {
        authorization = Unknown;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("unknown" or "low" or "medium" or "high"))
        {
            return false;
        }

        authorization = new ReviewAuthorization(normalized);
        return true;
    }

    public override string ToString() => Value;
}
