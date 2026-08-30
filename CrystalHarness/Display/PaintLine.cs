namespace CrystalHarness.Display;

/// <summary>
/// One screen row: Spectre markup plus the measured plain text.
/// Plain is the visual column text; padding uses this width, not markup tags.
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

    public PaintLine Fit(int width)
    {
        if (width < 1)
        {
            return Blank;
        }

        if (TextWidth.Measure(Plain) <= width)
        {
            return this;
        }

        return Colored(Theme.Chrome, TextWidth.Truncate(Plain, width));
    }
}
