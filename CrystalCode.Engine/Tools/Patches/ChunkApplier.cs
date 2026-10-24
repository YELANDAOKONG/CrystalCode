using System.Text;

namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// Applies the change blocks of one Update File section to a file's text.
/// Lines are matched exactly first, then ignoring trailing whitespace, then
/// ignoring surrounding whitespace, then ignoring typographic quote, dash,
/// and space differences. A model that retypes a line from memory often
/// differs only in those ways, and the looser passes run only when the
/// stricter ones find nothing.
/// </summary>
internal static class ChunkApplier
{
    private const char ByteOrderMark = '\uFEFF';

    private const int MaximumQuotedLines = 6;

    private static readonly Func<string, string, bool>[] Comparers =
    [
        static (file, wanted) => file == wanted,
        static (file, wanted) => file.TrimEnd() == wanted.TrimEnd(),
        static (file, wanted) => file.Trim() == wanted.Trim(),
        static (file, wanted) => Fold(file.Trim()) == Fold(wanted.Trim())
    ];

    /// <summary>
    /// Applies every chunk in order. The result keeps the file's byte order
    /// mark, its newline style, and whether it ended with a newline.
    /// <paramref name="regions"/> locates each change in the new text.
    /// </summary>
    public static bool TryApply(
        string original,
        IReadOnlyList<PatchChunk> chunks,
        out string updated,
        out IReadOnlyList<SnippetRegion> regions,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(chunks);
        updated = string.Empty;
        regions = [];
        var hasMark = original.Length > 0 && original[0] == ByteOrderMark;
        var body = hasMark ? original[1..] : original;
        var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = body.Length == 0 || body[^1] is '\n' or '\r';
        var lines = Split(body);

        if (!TryPlan(lines, chunks, out var edits, out error))
        {
            return false;
        }

        var ordered = edits.OrderBy(static edit => edit.Start).ToList();
        var located = new List<SnippetRegion>(ordered.Count);
        var shift = 0;
        foreach (var edit in ordered)
        {
            located.Add(new SnippetRegion(edit.Start + shift + 1, edit.Insert.Count));
            shift += edit.Insert.Count - edit.Remove;
        }

        for (var index = ordered.Count - 1; index >= 0; index--)
        {
            var edit = ordered[index];
            lines.RemoveRange(edit.Start, edit.Remove);
            lines.InsertRange(edit.Start, edit.Insert);
        }

        var text = new StringBuilder();
        if (hasMark)
        {
            text.Append(ByteOrderMark);
        }

        text.AppendJoin(newline, lines);
        if (lines.Count > 0 && endsWithNewline)
        {
            text.Append(newline);
        }

        updated = text.ToString();
        regions = located;
        return true;
    }

    private static List<string> Split(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static bool TryPlan(
        List<string> lines,
        IReadOnlyList<PatchChunk> chunks,
        out List<Edit> edits,
        out string error)
    {
        edits = [];
        error = string.Empty;
        var cursor = 0;
        foreach (var chunk in chunks)
        {
            var contextAt = -1;
            foreach (var context in chunk.Contexts)
            {
                var found = SeekContext(lines, context, cursor);
                if (found < 0)
                {
                    error = $"The context line was not found: {context}";
                    return false;
                }

                contextAt = found;
                cursor = found + 1;
            }

            if (chunk.OldLines.Count == 0)
            {
                var point = contextAt >= 0 ? contextAt + 1 : lines.Count;
                edits.Add(new Edit(point, 0, chunk.NewLines));
                continue;
            }

            // The block may start on its own context line, which models often
            // repeat as the first unchanged line.
            var from = contextAt >= 0 ? contextAt : cursor;
            var oldLines = chunk.OldLines;
            var newLines = chunk.NewLines;
            var start = Seek(lines, oldLines, from, chunk.AtEndOfFile);
            if (start < 0 && oldLines[^1].Length == 0)
            {
                // The block ended on a blank line that the file does not have
                // at that spot, so retry without it on both sides.
                oldLines = oldLines.Take(oldLines.Count - 1).ToList();
                if (newLines.Count > 0 && newLines[^1].Length == 0)
                {
                    newLines = newLines.Take(newLines.Count - 1).ToList();
                }

                start = oldLines.Count == 0 ? -1 : Seek(lines, oldLines, from, chunk.AtEndOfFile);
            }

            if (start < 0)
            {
                error = "The lines to change were not found"
                    + (chunk.AtEndOfFile ? " at the end of the file" : string.Empty)
                    + ":\n"
                    + string.Join('\n', chunk.OldLines.Take(MaximumQuotedLines));
                return false;
            }

            edits.Add(new Edit(start, oldLines.Count, newLines));
            cursor = start + oldLines.Count;
        }

        return true;
    }

    private static int SeekContext(List<string> lines, string context, int start)
    {
        var found = Seek(lines, [context], start, atEndOfFile: false);
        if (found >= 0)
        {
            return found;
        }

        // A context is often only the front of a longer declaration line.
        var prefix = context.Trim();
        for (var index = start; index < lines.Count; index++)
        {
            if (lines[index].Trim().StartsWith(prefix, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int Seek(List<string> lines, IReadOnlyList<string> wanted, int start, bool atEndOfFile)
    {
        foreach (var same in Comparers)
        {
            if (atEndOfFile)
            {
                // The block is tied to the end, so a looser match there wins
                // over an exact match earlier in the file.
                var last = lines.Count - wanted.Count;
                if (last >= start && Matches(lines, wanted, last, same))
                {
                    return last;
                }

                continue;
            }

            for (var offset = start; offset <= lines.Count - wanted.Count; offset++)
            {
                if (Matches(lines, wanted, offset, same))
                {
                    return offset;
                }
            }
        }

        return -1;
    }

    private static bool Matches(
        List<string> lines,
        IReadOnlyList<string> wanted,
        int offset,
        Func<string, string, bool> same)
    {
        for (var index = 0; index < wanted.Count; index++)
        {
            if (!same(lines[offset + index], wanted[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Maps typographic quotes, dashes, ellipses, and spaces to the ASCII
    /// characters they stand in for.
    /// </summary>
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            switch (character)
            {
                case '\u2018' or '\u2019' or '\u201A' or '\u201B':
                    folded.Append('\'');
                    break;
                case '\u201C' or '\u201D' or '\u201E' or '\u201F':
                    folded.Append('"');
                    break;
                case >= '\u2010' and <= '\u2015' or '\u2212':
                    folded.Append('-');
                    break;
                case '\u2026':
                    folded.Append("...");
                    break;
                case '\u00A0' or >= '\u2002' and <= '\u200A' or '\u202F' or '\u205F' or '\u3000':
                    folded.Append(' ');
                    break;
                default:
                    folded.Append(character);
                    break;
            }
        }

        return folded.ToString();
    }

    private readonly record struct Edit(int Start, int Remove, IReadOnlyList<string> Insert);
}
