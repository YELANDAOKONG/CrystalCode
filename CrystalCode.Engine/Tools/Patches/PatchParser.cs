namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// Reads the file-oriented patch text into file operations. The text is
/// wrapped in Begin and End markers and holds Add File, Update File, and
/// Delete File sections.
/// </summary>
internal sealed class PatchParser
{
    private const string BeginMarker = "*** Begin Patch";

    private const string EndMarker = "*** End Patch";

    private const string EndOfFileMarker = "*** End of File";

    private const string AddHeader = "*** Add File:";

    private const string DeleteHeader = "*** Delete File:";

    private const string UpdateHeader = "*** Update File:";

    private const string MoveHeader = "*** Move to:";

    private const string ChunkMarker = "@@";

    private readonly string[] _lines;

    private int _index;

    private PatchParser(string[] lines)
    {
        _lines = lines;
    }

    public static bool TryParse(
        string text,
        out IReadOnlyList<PatchOperation> operations,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(text);
        operations = [];
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var parser = new PatchParser(lines);
        if (!parser.TryReadAll(out var parsed, out error))
        {
            return false;
        }

        operations = parsed;
        return true;
    }

    private static bool IsBoundary(string line)
    {
        var trimmed = line.Trim();
        return trimmed == EndMarker
            || trimmed.StartsWith(AddHeader, StringComparison.Ordinal)
            || trimmed.StartsWith(DeleteHeader, StringComparison.Ordinal)
            || trimmed.StartsWith(UpdateHeader, StringComparison.Ordinal);
    }

    private static bool IsBodyLine(string line) =>
        line.Length > 0 && line[0] is ' ' or '-' or '+';

    private static bool TryReadPath(string line, string header, out string path, out string error)
    {
        path = line.Trim()[header.Length..].Trim();
        error = string.Empty;
        if (path.Length > 0)
        {
            return true;
        }

        error = $"'{header}' needs a path.";
        return false;
    }

    private bool TryReadAll(out List<PatchOperation> operations, out string error)
    {
        operations = [];
        error = string.Empty;
        SkipBlankLines();
        if (_index >= _lines.Length || _lines[_index].Trim() != BeginMarker)
        {
            error = $"The patch must start with '{BeginMarker}'.";
            return false;
        }

        _index++;
        while (true)
        {
            SkipBlankLines();
            if (_index >= _lines.Length)
            {
                error = $"The patch must end with '{EndMarker}'.";
                return false;
            }

            var line = _lines[_index].Trim();
            if (line == EndMarker)
            {
                _index++;
                break;
            }

            PatchOperation? operation;
            if (line.StartsWith(AddHeader, StringComparison.Ordinal))
            {
                operation = ReadAddition(out error);
            }
            else if (line.StartsWith(DeleteHeader, StringComparison.Ordinal))
            {
                operation = ReadDeletion(out error);
            }
            else if (line.StartsWith(UpdateHeader, StringComparison.Ordinal))
            {
                operation = ReadUpdate(out error);
            }
            else
            {
                error = $"Expected a file section but found: {line}";
                return false;
            }

            if (operation is null)
            {
                return false;
            }

            operations.Add(operation);
        }

        SkipBlankLines();
        if (_index < _lines.Length)
        {
            error = $"Unexpected text after '{EndMarker}'.";
            return false;
        }

        if (operations.Count == 0)
        {
            error = "The patch has no file sections.";
            return false;
        }

        return true;
    }

    private void SkipBlankLines()
    {
        while (_index < _lines.Length && _lines[_index].Trim().Length == 0)
        {
            _index++;
        }
    }

    private PatchAddition? ReadAddition(out string error)
    {
        if (!TryReadPath(_lines[_index], AddHeader, out var path, out error))
        {
            return null;
        }

        _index++;
        var contents = new List<string>();
        while (_index < _lines.Length && !IsBoundary(_lines[_index]))
        {
            var line = _lines[_index];
            if (line.Length == 0 && IsOnlyBlankUntilBoundary())
            {
                _index = _lines.Length;
                break;
            }

            if (!line.StartsWith('+'))
            {
                error = $"Every line of an Add File section must start with '+', but found: {line}";
                return null;
            }

            contents.Add(line[1..]);
            _index++;
        }

        // The loop above runs past trailing blank lines to the end of the
        // array. Step back to the closing marker so the caller sees it.
        if (_index >= _lines.Length)
        {
            _index = LastBoundaryIndex();
        }

        error = string.Empty;
        return new PatchAddition(path, contents);
    }

    private PatchDeletion? ReadDeletion(out string error)
    {
        if (!TryReadPath(_lines[_index], DeleteHeader, out var path, out error))
        {
            return null;
        }

        _index++;
        return new PatchDeletion(path);
    }

    private PatchUpdate? ReadUpdate(out string error)
    {
        if (!TryReadPath(_lines[_index], UpdateHeader, out var path, out error))
        {
            return null;
        }

        _index++;
        string? movePath = null;
        if (_index < _lines.Length
            && _lines[_index].Trim().StartsWith(MoveHeader, StringComparison.Ordinal))
        {
            if (!TryReadPath(_lines[_index], MoveHeader, out var moved, out error))
            {
                return null;
            }

            movePath = moved;
            _index++;
        }

        var chunks = new List<PatchChunk>();
        while (_index < _lines.Length && !IsBoundary(_lines[_index]))
        {
            if (_lines[_index].Trim().Length == 0 && chunks.Count == 0 && !HasBodyAhead())
            {
                _index++;
                continue;
            }

            var chunk = ReadChunk(out error);
            if (chunk is null)
            {
                return null;
            }

            chunks.Add(chunk);
        }

        if (chunks.Count == 0 && movePath is null)
        {
            error = $"The Update File section for {path} has no changes.";
            return null;
        }

        error = string.Empty;
        return new PatchUpdate(path, movePath, chunks);
    }

    private PatchChunk? ReadChunk(out string error)
    {
        var contexts = new List<string>();
        while (_index < _lines.Length && _lines[_index].StartsWith(ChunkMarker, StringComparison.Ordinal))
        {
            var context = _lines[_index][ChunkMarker.Length..];
            if (context.StartsWith(' '))
            {
                context = context[1..];
            }

            if (context.Trim().Length > 0)
            {
                contexts.Add(context.TrimEnd());
            }

            _index++;
        }

        var oldLines = new List<string>();
        var newLines = new List<string>();
        var atEndOfFile = false;
        var bodyLines = 0;
        while (_index < _lines.Length)
        {
            var line = _lines[_index];
            if (line.Length == 0)
            {
                // A line with no characters at all is a blank context line.
                oldLines.Add(string.Empty);
                newLines.Add(string.Empty);
            }
            else if (IsBodyLine(line))
            {
                var text = line[1..];
                if (line[0] != '+')
                {
                    oldLines.Add(text);
                }

                if (line[0] != '-')
                {
                    newLines.Add(text);
                }
            }
            else if (line.Trim() == EndOfFileMarker)
            {
                atEndOfFile = true;
                _index++;
                break;
            }
            else if (IsBoundary(line) || line.StartsWith(ChunkMarker, StringComparison.Ordinal))
            {
                break;
            }
            else
            {
                error = $"Expected a line starting with ' ', '-', or '+' but found: {line}";
                return null;
            }

            bodyLines++;
            _index++;
        }

        if (bodyLines == 0)
        {
            error = "A change block has no lines.";
            return null;
        }

        error = string.Empty;
        return new PatchChunk(contexts, oldLines, newLines, atEndOfFile);
    }

    private bool HasBodyAhead()
    {
        for (var index = _index; index < _lines.Length; index++)
        {
            var line = _lines[index];
            if (IsBoundary(line))
            {
                return false;
            }

            if (line.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOnlyBlankUntilBoundary()
    {
        for (var index = _index; index < _lines.Length; index++)
        {
            if (IsBoundary(_lines[index]))
            {
                return true;
            }

            if (_lines[index].Length > 0)
            {
                return false;
            }
        }

        return true;
    }

    private int LastBoundaryIndex()
    {
        for (var index = _lines.Length - 1; index >= 0; index--)
        {
            if (IsBoundary(_lines[index]))
            {
                return index;
            }
        }

        return _lines.Length;
    }
}
