using System.Text;
using CrystalCode.Display.Paint;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalCode.Display.Shell;

/// <summary>
/// One frame row as styled segments that are already cropped to the terminal
/// width. Spectre wraps markup that it measures as wider than the console and
/// writes a line feed, which would shift every row below it. A row built here
/// is never re-wrapped: if a width estimate is off, the tail is cropped.
/// </summary>
internal sealed class FrameRow : IRenderable
{
    private const int RasterWidth = 4096;

    private readonly IReadOnlyList<Segment> _segments;

    private FrameRow(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
    }

    public bool IsEmpty => _segments.Count == 0;

    /// <summary>
    /// Markup that is safe to hand to Spectre: tabs expanded and every control
    /// character removed.
    /// </summary>
    public static string SafeMarkup(PaintLine line)
    {
        var markup = line.Markup ?? string.Empty;
        if (markup.Length == 0)
        {
            return markup;
        }

        if (markup.Contains('\t'))
        {
            markup = TextWidth.ExpandTabs(markup);
        }

        return TerminalText.StripControls(markup);
    }

    public static FrameRow Create(PaintLine line, int width, IAnsiConsole raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        var markup = SafeMarkup(line);
        if (markup.Length == 0 || width < 1)
        {
            return new FrameRow([]);
        }

        var segments = new List<Segment>();
        var used = 0;
        foreach (var segment in new Markup(markup).GetSegments(raster))
        {
            if (segment.IsLineBreak || segment.IsControlCode)
            {
                continue;
            }

            var cells = segment.CellCount();
            if (used + cells <= width)
            {
                segments.Add(segment);
                used += cells;
                continue;
            }

            var cropped = Crop(segment, width - used);
            if (cropped is not null)
            {
                segments.Add(cropped);
            }

            break;
        }

        return new FrameRow(segments);
    }

    public static IAnsiConsole CreateRaster() => WidgetPaint.CreateConsole(RasterWidth);

    /// <summary>Total terminal cells of the row as Spectre counts them.</summary>
    public int CellCount()
    {
        var total = 0;
        foreach (var segment in _segments)
        {
            total += segment.CellCount();
        }

        return total;
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        new(0, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => _segments;

    private static Segment? Crop(Segment segment, int budget)
    {
        if (budget < 1)
        {
            return null;
        }

        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in segment.Text.EnumerateRunes())
        {
            var text = rune.ToString();
            var cells = new Segment(text).CellCount();
            if (used + cells > budget)
            {
                break;
            }

            builder.Append(text);
            used += cells;
        }

        return builder.Length == 0 ? null : new Segment(builder.ToString(), segment.Style);
    }
}
