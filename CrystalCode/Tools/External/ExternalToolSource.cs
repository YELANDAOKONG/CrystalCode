namespace CrystalCode.Tools.External;

/// <summary>
/// Location that contributed an external tool set.
/// </summary>
public sealed record ExternalToolSource
{
    public static ExternalToolSource Home { get; } = new("home");

    public static ExternalToolSource Project { get; } = new("project");

    public ExternalToolSource(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
