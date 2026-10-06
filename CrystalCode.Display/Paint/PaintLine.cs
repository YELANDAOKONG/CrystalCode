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
        if (TerminalText.HasControls(plain))
        {
            // SanitizeLine turns a tab into one space. A row keeps tabs so Fit
            // can expand them; breaks still become spaces, without carriage-return
            // overwrite.
            plain = plain.Contains('\t')
                ? FlattenBreaksKeepingTabs(plain)
                : TerminalText.SanitizeLine(plain);
        }

        return new PaintLine($"[{color}]{MarkupText.Escape(plain)}[/]", plain);
    }

    private static string FlattenBreaksKeepingTabs(string plain)
    {
        var parts = plain.Split('\t');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = TerminalText.SanitizeLine(parts[i]);
        }

        return string.Join('\t', parts);
    }

    /// <summary>
    /// Fits the row to the width. A row never leaves here holding a control
    /// character: the markup and plain text are stripped as a backstop for any
    /// caller that built a row by hand.
    /// </summary>
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
        if (TerminalText.HasControls(line.Markup) || TerminalText.HasControls(line.Plain))
        {
            line = new PaintLine(
                TerminalText.StripControls(line.Markup),
                TerminalText.StripControls(line.Plain));
        }

        if (TextWidth.Measure(line.Plain) <= width)
        {
            return line;
        }

        return Colored(Theme.Chrome, TextWidth.Truncate(line.Plain, width));
    }
}
