namespace CrystalCode.Engine.Configuration;

/// <summary>
/// How an Anthropic endpoint asks for thinking text. Absent configuration
/// uses <see cref="Summarized"/> and is not written back.
/// </summary>
public sealed record ThinkingDisplay
{
    public static ThinkingDisplay Summarized { get; } = new("summarized");

    public static ThinkingDisplay Omitted { get; } = new("omitted");

    public ThinkingDisplay(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ThinkingDisplay Parse(string value)
    {
        var display = new ThinkingDisplay(value);
        if (display == Summarized)
        {
            return Summarized;
        }

        if (display == Omitted)
        {
            return Omitted;
        }

        throw new ArgumentException(
            "Thinking display must be summarized or omitted.",
            nameof(value));
    }

    public override string ToString() => Value;
}
