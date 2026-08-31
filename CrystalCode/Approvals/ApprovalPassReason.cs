namespace CrystalCode.Approvals;

/// <summary>
/// Why a classified tool call executed without asking the operator.
/// </summary>
public sealed record ApprovalPassReason
{
    public static ApprovalPassReason Policy { get; } = new("policy");

    public static ApprovalPassReason Grant { get; } = new("grant");

    public static ApprovalPassReason Review { get; } = new("review");

    public ApprovalPassReason(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
