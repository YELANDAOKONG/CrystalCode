using System.Text.RegularExpressions;

namespace CrystalCode.Display.Paint;

/// <summary>A validated <see cref="GaugeRule"/> ready to test values.</summary>
internal sealed class GaugeMatcher(GaugeRule rule, Regex? pattern)
{
    public string Style { get; } = rule.Style.Trim();

    /// <summary>True for a from/to rule, false for a match or regex rule.</summary>
    public bool IsRange { get; } = rule.Match.Count == 0 && rule.Pattern is null;

    public bool Holds(double value) =>
        IsRange && value >= (rule.From ?? 0) && value <= (rule.To ?? double.PositiveInfinity);

    /// <summary>
    /// Tests a text value. A from/to rule holds when <paramref name="number"/>,
    /// the value as a number, is in range; it never holds without one.
    /// </summary>
    public bool Holds(string value, double? number = null)
    {
        if (IsRange)
        {
            return number is { } known && Holds(known);
        }

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
