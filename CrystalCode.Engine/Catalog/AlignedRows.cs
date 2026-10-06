namespace CrystalCode.Engine.Catalog;

/// <summary>Pads columns for plain-text tables. The last column is not padded.</summary>
internal static class AlignedRows
{
    public static IReadOnlyList<string> Format(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return [string.Join("  ", headers)];
        }

        var widths = new int[headers.Count];
        for (var column = 0; column < headers.Count; column++)
        {
            widths[column] = headers[column].Length;
            foreach (var row in rows)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        var lines = new List<string> { Join(headers, widths) };
        foreach (var row in rows)
        {
            lines.Add(Join(row, widths));
        }

        return lines;
    }

    public static IReadOnlyList<string> Fields(IReadOnlyList<(string Label, string Value)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var width = 0;
        foreach (var field in fields)
        {
            width = Math.Max(width, field.Label.Length);
        }

        return fields
            .Select(field => field.Label.PadRight(width) + "  " + field.Value)
            .ToArray();
    }

    private static string Join(IReadOnlyList<string> values, IReadOnlyList<int> widths) =>
        string.Join(
            "  ",
            values.Select(
                (value, index) => index == values.Count - 1
                    ? value
                    : value.PadRight(widths[index])));
}
