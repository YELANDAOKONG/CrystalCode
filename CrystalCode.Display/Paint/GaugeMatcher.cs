using System.Text.RegularExpressions;

namespace CrystalCode.Display.Paint;

/// <summary>A validated <see cref="GaugeRule"/> ready to test values.</summary>
internal sealed class GaugeMatcher(GaugeRule rule, Regex? pattern)
{
    public string Style { get; } = rule.Style.Trim();

    public bool Holds(double value) =>
        rule.From is { } from && rule.To is { } to && value >= from && value <= to;

    public bool Holds(string value)
    {
        foreach (var text in rule.Match)
        {
            if (text == "*" || text.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        try
        {
            return pattern is not null && pattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
