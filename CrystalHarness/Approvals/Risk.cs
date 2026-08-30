namespace CrystalHarness.Approvals;

/// <summary>
/// Classifies how dangerous a tool call is.
/// </summary>
public sealed record Risk
{
    public static Risk Read { get; } = new("read");

    public static Risk Write { get; } = new("write");

    public static Risk Privileged { get; } = new("privileged");

    public static Risk Forbidden { get; } = new("forbidden");

    public Risk(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
