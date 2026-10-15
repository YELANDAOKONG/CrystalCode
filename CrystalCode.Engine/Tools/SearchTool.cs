using System.Text;
using System.Text.RegularExpressions;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Ranks workspace files by matching a regular expression against their
/// relative paths and text contents.
/// </summary>
public sealed class SearchTool : ITool
{
    public const string ToolName = "search";

    private static readonly string ToolDescription =
        "Finds candidate workspace files by matching a .NET regular expression against both relative paths "
        + "and file contents. Use it first when you do not know which files matter, then read the best candidates. "
        + "One line per file, ranked with name matches first, then by matching-line count; each file line shows "
        + $"its count (capped at {WorkspaceLimits.MaximumSearchMatchesPerFile} with a + suffix) and up to "
        + $"{WorkspaceLimits.MaximumSearchPreviewLinesPerFile} matching lines as previews. "
        + "Case-insensitive by default; set caseSensitive true for exact case. "
        + "Optional path and file-name glob narrow the search. "
        + $"Use offset (1-based) and limit to page through the ranked files; limit defaults to "
        + $"{WorkspaceLimits.DefaultSearchFileLimit} files and is capped at {WorkspaceLimits.MaximumSearchFiles}. "
        + "Skips binary files, credential paths, and common build directories. "
        + "Batch independent searches in parallel.";

    private readonly Workspace _workspace;

    public SearchTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                $$"""
                {
                  "type": "object",
                  "properties": {
                    "query": {
                      "type": "string",
                      "description": ".NET regular expression matched against workspace-relative paths and file contents."
                    },
                    "path": {
                      "type": "string",
                      "description": "Optional file or directory to search."
                    },
                    "glob": {
                      "type": "string",
                      "description": "Optional file-name glob such as *.cs."
                    },
                    "caseSensitive": {
                      "type": "boolean",
                      "description": "Match case exactly. Defaults to false (case-insensitive)."
                    },
                    "offset": {
                      "type": "integer",
                      "description": "1-based candidate file to start from."
                    },
                    "limit": {
                      "type": "integer",
                      "description": "Maximum number of candidate files to return, up to {{WorkspaceLimits.MaximumSearchFiles}}; defaults to {{WorkspaceLimits.DefaultSearchFileLimit}}."
                    }
                  },
                  "required": ["query"]
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (!ToolArguments.TryReadRequiredString(call.Arguments, "query", out var query)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "path", out var relativePath)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "glob", out var globText)
            || !ToolArguments.TryReadOptionalBoolean(call.Arguments, "caseSensitive", out var caseSensitive)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "offset", out var offset)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "limit", out var limit))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include query, with optional path and glob strings, "
                    + "caseSensitive boolean, and offset and limit integers.",
                    ToolResultStatus.Failure));
        }

        if (offset is <= 0 || limit is <= 0)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "offset and limit must be positive when supplied.",
                    ToolResultStatus.Failure));
        }

        var options = RegexOptions.CultureInvariant;
        if (caseSensitive != true)
        {
            options |= RegexOptions.IgnoreCase;
        }

        Regex regex;
        try
        {
            regex = new Regex(query, options, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Invalid regular expression: " + exception.Message,
                    ToolResultStatus.Failure));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = null;
        }

        GlobPattern? fileGlob = null;
        if (!string.IsNullOrWhiteSpace(globText)
            && !GlobPattern.TryCreate(globText, out fileGlob, out var globError))
        {
            return ValueTask.FromResult(new ToolOutput(globError, ToolResultStatus.Failure));
        }

        if (!TextSearchFiles.TryCollect(_workspace, relativePath, out var files, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        var candidates = new List<Candidate>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = _workspace.ToRelative(file);
            if (fileGlob is not null
                && !fileGlob.IsMatch(relative)
                && !fileGlob.IsMatch(Path.GetFileName(file)))
            {
                continue;
            }

            var nameMatch = TryIsMatch(regex, relative);
            var count = 0;
            var capped = false;
            var previews = new List<PreviewLine>();
            if (TextSearchFiles.IsSearchable(file))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (!TryIsMatch(regex, line))
                    {
                        continue;
                    }

                    if (count >= WorkspaceLimits.MaximumSearchMatchesPerFile)
                    {
                        capped = true;
                        break;
                    }

                    count++;
                    if (previews.Count < WorkspaceLimits.MaximumSearchPreviewLinesPerFile)
                    {
                        previews.Add(new PreviewLine(lineNumber, line));
                    }
                }
            }

            if (nameMatch || count > 0)
            {
                candidates.Add(new Candidate(relative, nameMatch, count, capped, previews));
            }
        }

        if (candidates.Count == 0)
        {
            return ValueTask.FromResult(new ToolOutput("No matches."));
        }

        candidates.Sort(static (left, right) =>
        {
            if (left.NameMatch != right.NameMatch)
            {
                return left.NameMatch ? -1 : 1;
            }

            if (left.Count != right.Count)
            {
                return right.Count.CompareTo(left.Count);
            }

            return string.CompareOrdinal(left.RelativePath, right.RelativePath);
        });

        var start = offset ?? 1;
        if (start > candidates.Count)
        {
            var noun = candidates.Count == 1 ? "file" : "files";
            return ValueTask.FromResult(
                new ToolOutput(
                    $"Search matched {candidates.Count} {noun}; offset {start} is past the end.",
                    ToolResultStatus.Failure));
        }

        var maximum = Math.Min(
            limit ?? WorkspaceLimits.DefaultSearchFileLimit,
            WorkspaceLimits.MaximumSearchFiles);
        var pageCount = Math.Min(maximum, candidates.Count - start + 1);
        var builder = new StringBuilder();
        for (var index = start - 1; index < start - 1 + pageCount; index++)
        {
            AppendCandidate(builder, candidates[index]);
        }

        if (start - 1 + pageCount < candidates.Count)
        {
            ToolOutputText.AppendLine(
                builder,
                $"[showing {pageCount} of {candidates.Count} files; continue with offset {start + pageCount}]");
        }

        return ValueTask.FromResult(new ToolOutput(builder.ToString().TrimEnd()));
    }

    private static bool TryIsMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static void AppendCandidate(StringBuilder builder, Candidate candidate)
    {
        var header = new StringBuilder();
        header.Append(candidate.RelativePath);
        if (candidate.NameMatch)
        {
            header.Append("  (name)");
        }

        if (candidate.Count > 0)
        {
            header.Append(candidate.NameMatch ? " " : "  ");
            header.Append(candidate.Count);
            if (candidate.Capped)
            {
                header.Append('+');
            }

            header.Append(candidate.Count == 1 && !candidate.Capped ? " match" : " matches");
        }

        ToolOutputText.AppendLine(builder, header.ToString());
        foreach (var preview in candidate.Previews)
        {
            var text = preview.Text.Length > WorkspaceLimits.MaximumSearchPreviewCharacters
                ? preview.Text[..WorkspaceLimits.MaximumSearchPreviewCharacters] + "..."
                : preview.Text;
            ToolOutputText.AppendLine(builder, $"  {preview.LineNumber}: {text}");
        }
    }

    private sealed record Candidate(
        string RelativePath,
        bool NameMatch,
        int Count,
        bool Capped,
        List<PreviewLine> Previews);

    private sealed record PreviewLine(int LineNumber, string Text);
}
