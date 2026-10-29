namespace CrystalCode.Display.Paint;

/// <summary>
/// One range of a gauge such as <c>context</c>: a percentage from
/// <paramref name="From"/> through <paramref name="To"/>, both inclusive,
/// is painted with the Spectre style <paramref name="Style"/>.
/// </summary>
public sealed record GaugeBand(double From, double To, string Style);
