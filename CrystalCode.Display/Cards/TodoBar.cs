using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Cards;

/// <summary>
/// Sticky todo list painted above the progress row and status bar.
/// </summary>
public static class TodoBar
{
    public const int MaximumVisible = 4;

    public static IReadOnlyList<PaintLine> Lines(IReadOnlyList<TodoBarItem> items, int width)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return [];
        }

        var lines = new List<PaintLine>
        {
            Fit(PaintLine.Colored(Theme.Chrome, "  Todos"), width)
        };
        var visible = Math.Min(items.Count, MaximumVisible);
        for (var i = 0; i < visible; i++)
        {
            lines.Add(ItemLine(items[i], width));
        }

        if (items.Count > MaximumVisible)
        {
            var more = items.Count - MaximumVisible;
            lines.Add(Fit(PaintLine.Colored(Theme.Muted, $"  +{more} more"), width));
        }

        return lines;
    }

    private static PaintLine ItemLine(TodoBarItem item, int width)
    {
        var mark = item.Mark.Length == 0 ? " " : item.Mark[..1];
        var color = TodoMarks.Color(mark[0]) ?? Theme.Chrome;
        var plain = $"  [{mark}] {item.Content}";
        if (TextWidth.Measure(plain) > Math.Max(width, 1))
        {
            plain = TextWidth.Truncate(plain, Math.Max(width, 1));
        }

        return PaintLine.Colored(color, plain);
    }

    private static PaintLine Fit(PaintLine line, int width)
    {
        if (TextWidth.Measure(line.Plain) <= Math.Max(width, 1))
        {
            return line;
        }

        return PaintLine.Colored(Theme.Chrome, TextWidth.Truncate(line.Plain, Math.Max(width, 1)));
    }
}
