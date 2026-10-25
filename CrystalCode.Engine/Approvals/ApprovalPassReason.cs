namespace CrystalCode.Engine.Approvals;

/// <summary>
/// Why a classified tool call executed without asking the operator.
/// </summary>
public sealed record ApprovalPassReason
{
    public static ApprovalPassReason Policy { get; } = new("policy");

    public static ApprovalPassReason Grant { get; } = new("grant");

    public static ApprovalPassReason Review { get; } = new("review");

    /// <summary>
    /// The primary reviewer allowed the call and the extended reviewer agreed.
    /// </summary>
    public static ApprovalPassReason Extended { get; } = new("extended review");

    public ApprovalPassReason(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public bool IsReview => this == Review || this == Extended;

    public override string ToString() => Value;
}
