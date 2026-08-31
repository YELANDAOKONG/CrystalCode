namespace CrystalCode.Approvals;

/// <summary>
/// How long an approval grant remains valid.
/// </summary>
public sealed record GrantScope
{
    public static GrantScope Once { get; } = new("once");

    public static GrantScope Session { get; } = new("session");

    public static GrantScope Persistent { get; } = new("persistent");

    public GrantScope(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
