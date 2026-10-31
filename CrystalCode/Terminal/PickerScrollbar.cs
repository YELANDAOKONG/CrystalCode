using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Shell;

namespace CrystalCode.Terminal;

/// <summary>
/// Puts the transcript scrollbar beside a picker list whose entries take two
/// rows each by default: a title row and a detail row. A list of single-row entries passes 1.
/// </summary>
internal static class PickerScrollbar
{
    public static IRenderable Wrap(List<IRenderable> list, int count, int start, bool barred, int entryRows = 2)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (!barred)
        {
            return new Rows(list);
        }

        // The thumb is measured in rows, not entries.
        var thumb = TranscriptScrollbar.ThumbFromTop(count * entryRows, list.Count, start * entryRows);
        var bar = string.Join(
            '\n',
            Enumerable.Range(0, list.Count).Select(row => TranscriptScrollbar.Cell(row, thumb).Markup));
        var grid = new Grid().Expand();
        grid.AddColumn(new GridColumn { Padding = new Padding(0, 0, TranscriptScrollbar.Columns - 1, 0) });
        grid.AddColumn(new GridColumn { Width = 1, NoWrap = true, Padding = new Padding(0, 0, 0, 0) });
        grid.AddRow(new Rows(list), new Markup(bar));
        return grid;
    }
}
