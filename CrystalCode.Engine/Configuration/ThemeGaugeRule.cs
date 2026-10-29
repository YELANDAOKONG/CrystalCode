namespace CrystalCode.Engine.Configuration;

/// <summary>
/// One rule of a theme gauge, in the priority order of the file. A numeric
/// rule has <paramref name="From"/> and <paramref name="To"/>, both inclusive;
/// a missing end is left null for the front end to fill.
/// A value rule has <paramref name="Match"/> (plain texts, <c>*</c> for all) or
/// <paramref name="Pattern"/> (a regular expression), never both and never
/// together with a range. The engine reads the shape only; a front end decides
/// which gauges exist and what a value means.
/// </summary>
public sealed record ThemeGaugeRule(
    double? From,
    double? To,
    IReadOnlyList<string> Match,
    string? Pattern,
    string Color);
