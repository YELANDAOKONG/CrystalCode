namespace CrystalCode.Display.Paint;

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

        var line = Plain.Contains('\t') || Markup.Contains('\t')
            ? new PaintLine(
                TextWidth.ExpandTabs(Markup),
                TextWidth.ExpandTabs(Plain))
            : this;
        if (TextWidth.Measure(line.Plain) <= width)
        {
            return line;
        }

        return Colored(Theme.Chrome, TextWidth.Truncate(line.Plain, width));
    }
}
