namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Selects how one exec tool's stdout is interpreted.
/// </summary>
public sealed record ExternalToolOutputMode
{
    public static ExternalToolOutputMode Text { get; } = new("text");

    public static ExternalToolOutputMode Content { get; } = new("content");

    public ExternalToolOutputMode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static bool TryParse(string value, out ExternalToolOutputMode output)
    {
        output = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parsed = new ExternalToolOutputMode(value);
        if (parsed == Text || parsed == Content)
        {
            output = parsed;
            return true;
        }

        return false;
    }

    public override string ToString() => Value;
}
