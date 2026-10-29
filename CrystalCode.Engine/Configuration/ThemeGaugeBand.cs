namespace CrystalCode.Engine.Configuration;

/// <summary>
/// One range of a theme gauge: a value from <paramref name="From"/> through
/// <paramref name="To"/>, both inclusive, takes <paramref name="Color"/>. The
/// engine reads the shape only; a front end decides what the range and the
/// color mean.
/// </summary>
public sealed record ThemeGaugeBand(double From, double To, string Color);
