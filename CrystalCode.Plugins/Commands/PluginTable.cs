using System.Text;

namespace CrystalCode.Plugins.Commands;

/// <summary>A header row and data rows of equal width.</summary>
public sealed record PluginTable : PluginBlock
{
    public PluginTable(IEnumerable<string> columns, IEnumerable<IEnumerable<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        Columns = [.. columns.Select(PluginDocumentLimits.Clip)];
        if (Columns.Count == 0 || Columns.Count > PluginDocumentLimits.MaximumColumns)
        {
            throw new ArgumentException(
                $"A table needs 1 to {PluginDocumentLimits.MaximumColumns} columns.",
                nameof(columns));
        }

        var cut = PluginDocumentLimits.Take(
            rows.Select(static row => (IReadOnlyList<string>)[.. row]),
            out var omitted);
        foreach (var row in cut)
        {
            if (row.Count != Columns.Count)
            {
                throw new ArgumentException("Every row must have one cell per column.", nameof(rows));
            }
        }

        Rows = [.. cut.Select(static row => (IReadOnlyList<string>)[.. row.Select(PluginDocumentLimits.Clip)])];
        Omitted = omitted;
    }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    /// <summary>Gets how many rows past the limit were dropped.</summary>
    public int Omitted { get; }

    internal override void Append(StringBuilder text)
    {
        var widths = Columns.Select((column, index) =>
            Math.Max(column.Length, Rows.Count == 0 ? 0 : Rows.Max(row => row[index].Length))).ToArray();
        AppendRow(text, Columns, widths);
        AppendRow(text, widths.Select(static width => new string('-', width)).ToArray(), widths);
        foreach (var row in Rows)
        {
            AppendRow(text, row, widths);
        }

        if (Omitted > 0)
        {
            text.AppendLine($"({Omitted} more omitted)");
        }
    }

    private static void AppendRow(StringBuilder text, IReadOnlyList<string> cells, int[] widths)
    {
        var line = string.Join("  ", cells.Select((cell, index) => cell.PadRight(widths[index])));
        text.AppendLine(line.TrimEnd());
    }
}
