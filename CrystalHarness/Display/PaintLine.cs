namespace CrystalHarness.Display;

/// <summary>
/// One screen row: Spectre markup plus the measured plain text.
/// </summary>
public readonly record struct PaintLine(string Markup, string Plain)
{
    public static PaintLine Blank { get; } = new(string.Empty, string.Empty);

    public static PaintLine Colored(string color, string plain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        ArgumentNullException.ThrowIfNull(plain);
        return new PaintLine($"[{color}]{MarkupText.Escape(plain)}[/]", plain);
    }
}
