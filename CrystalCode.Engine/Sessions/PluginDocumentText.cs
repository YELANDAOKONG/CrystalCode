using System.Text;

namespace CrystalCode.Engine.Sessions;

/// <summary>Plain-text form of a <see cref="TranscriptDocument"/>, for front ends that do not draw cards.</summary>
internal static class PluginDocumentText
{
    public static string Format(TranscriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = new StringBuilder();
        text.AppendLine(document.Title);
        foreach (var block in document.Blocks)
        {
            text.AppendLine();
            Append(text, block);
        }

        return text.ToString().TrimEnd();
    }

    private static void Append(StringBuilder text, TranscriptDocumentBlock block)
    {
        switch (block.Kind)
        {
            case "paragraph":
                foreach (var line in block.Lines)
                {
                    text.AppendLine(line);
                }

                break;
            case "list":
                foreach (var line in block.Lines)
                {
                    text.Append("- ").AppendLine(line);
                }

                break;
            case "keyValues":
                var keyWidth = block.Rows.Count == 0 ? 0 : block.Rows.Max(static row => row[0].Length);
                foreach (var row in block.Rows)
                {
                    text.Append(row[0].PadRight(keyWidth)).Append("  ").AppendLine(row[1]);
                }

                break;
            case "table":
                var widths = block.Columns.Select((column, index) =>
                    Math.Max(column.Length, block.Rows.Count == 0 ? 0 : block.Rows.Max(row => row[index].Length))).ToArray();
                Row(text, block.Columns, widths);
                Row(text, widths.Select(static width => new string('-', width)).ToArray(), widths);
                foreach (var row in block.Rows)
                {
                    Row(text, row, widths);
                }

                break;
        }

        if (block.Omitted > 0)
        {
            text.AppendLine($"({block.Omitted} more omitted)");
        }
    }

    private static void Row(StringBuilder text, IReadOnlyList<string> cells, int[] widths) =>
        text.AppendLine(string.Join("  ", cells.Select((cell, index) => cell.PadRight(widths[index]))).TrimEnd());
}
