namespace CrystalCode.Engine.Approvals;

/// <summary>
/// Which reviewer is judging a call: the primary Review or Audit reviewer,
/// or the extended reviewer that checks a primary allow.
/// </summary>
public sealed record ApprovalReviewStage
{
    public static ApprovalReviewStage Primary { get; } = new("primary");

    public static ApprovalReviewStage Extended { get; } = new("extended");

    public ApprovalReviewStage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
