namespace CrystalCode.Approvals;

/// <summary>
/// Classifies where a tool call takes effect.
/// </summary>
public sealed record Authority
{
    public static Authority Workspace { get; } = new("workspace");

    public static Authority OutsideWorkspace { get; } = new("outside_workspace");

    public static Authority Network { get; } = new("network");

    public static Authority PrivilegedEscalation { get; } = new("privileged_escalation");

    public Authority(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
