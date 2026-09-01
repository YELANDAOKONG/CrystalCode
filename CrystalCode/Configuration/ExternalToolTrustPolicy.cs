namespace CrystalCode.Configuration;

/// <summary>
/// Selects whether one external-tool source follows author approval declarations.
/// </summary>
public sealed record ExternalToolTrustPolicy
{
    public static ExternalToolTrustPolicy Author { get; } = new("author");

    public static ExternalToolTrustPolicy Host { get; } = new("host");

    public ExternalToolTrustPolicy(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ExternalToolTrustPolicy Parse(string value)
    {
        var policy = new ExternalToolTrustPolicy(value);
        if (policy == Author || policy == Host)
        {
            return policy;
        }

        throw new ArgumentException(
            "External tool trust policy must be author or host.",
            nameof(value));
    }

    public override string ToString() => Value;
}
