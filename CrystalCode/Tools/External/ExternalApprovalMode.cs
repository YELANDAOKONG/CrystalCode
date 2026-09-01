namespace CrystalCode.Tools.External;

/// <summary>
/// Author-declared approval behavior for an external tool.
/// </summary>
public sealed record ExternalApprovalMode
{
    public static ExternalApprovalMode Inherit { get; } = new("inherit");

    public static ExternalApprovalMode Always { get; } = new("always");

    public ExternalApprovalMode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static bool TryParse(string? value, out ExternalApprovalMode mode)
    {
        mode = Inherit;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var parsed = new ExternalApprovalMode(value);
        if (parsed == Inherit || parsed == Always)
        {
            mode = parsed;
            return true;
        }

        return false;
    }

    public override string ToString() => Value;
}
