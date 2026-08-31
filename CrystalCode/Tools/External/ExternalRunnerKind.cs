namespace CrystalCode.Tools.External;

/// <summary>
/// Selects how one tool set invokes contributed tools.
/// </summary>
public sealed record ExternalRunnerKind
{
    public static ExternalRunnerKind Exec { get; } = new("exec");

    public static ExternalRunnerKind Dotnet { get; } = new("dotnet");

    public ExternalRunnerKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static bool TryParse(string value, out ExternalRunnerKind runner)
    {
        runner = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parsed = new ExternalRunnerKind(value);
        if (parsed == Exec || parsed == Dotnet)
        {
            runner = parsed;
            return true;
        }

        return false;
    }

    public override string ToString() => Value;
}
