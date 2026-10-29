namespace CrystalCode.Display.Paint;

/// <summary>
/// One rule of a gauge. A numeric rule (<c>context</c>) uses
/// <paramref name="From"/> through <paramref name="To"/>, both inclusive. A
/// value rule (<c>thinking</c>, <c>tier</c>, <c>speed</c>) uses
/// <paramref name="Match"/> (plain texts compared without case, <c>*</c> for
/// every value) or <paramref name="Pattern"/> (a regular expression). The
/// Spectre style <paramref name="Style"/> paints what the rule holds.
/// </summary>
public sealed record GaugeRule(
    double? From,
    double? To,
    IReadOnlyList<string> Match,
    string? Pattern,
    string Style);
