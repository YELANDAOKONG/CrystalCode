using System.Text;
using System.Text.RegularExpressions;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Searches workspace text files with a regular expression.
/// </summary>
public sealed class GrepTool : ITool
{
    public const string ToolName = "grep";

    private const string ContentMode = "content";

    private const string FilesMode = "files";

    private const string CountMode = "count";

    private static readonly string ToolDescription =
        "Searches text files under the requested path with a .NET regular expression. "
        + "Optional path may be workspace-relative or absolute and defaults to the workspace root; an optional file-name glob narrows the search. "
        + "output_mode content (the default) prints path:line:text. files lists only the paths that match. count prints path:count for each matching file. "
        + $"context adds up to {WorkspaceLimits.MaximumGrepContextLines} lines before and after each match in content mode, printed as path-line-text, with -- between separate groups. "
        + $"limit caps matching lines in content mode and files in files and count modes; it defaults to {WorkspaceLimits.DefaultGrepMatches} and is capped at {WorkspaceLimits.MaximumGrepMatches}. "
        + "Session read or write access permits searching an outside directory; unlisted outside paths may require approval. "
        + "Searches only the requested root, not all authorized directories. "
        + "Skips binary files and common build directories. "
        + "Chain glob and grep for open-ended search. Batch independent searches in parallel.";

    private readonly Workspace _workspace;

    public GrepTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        Definition = ToolDefinition.Json(
            ToolName,
            ToolSchema.Parse(
                $$"""
                {
                  "type": "object",
                  "properties": {
                    "pattern": {
                      "type": "string",
                      "description": ".NET regular expression searched line by line."
                    },
                    "path": {
                      "type": "string",
                      "description": "Optional workspace-relative or absolute file or directory. Defaults to the workspace root."
                    },
                    "glob": {
                      "type": "string",
                      "description": "Optional file-name glob such as *.cs."
                    },
                    "output_mode": {
                      "type": "string",
                      "enum": ["{{ContentMode}}", "{{FilesMode}}", "{{CountMode}}"],
                      "description": "content prints matching lines (default). files lists matching paths only. count prints the matching-line count per file."
                    },
                    "context": {
                      "type": "integer",
                      "description": "Lines of context before and after each match in content mode, 0 to {{WorkspaceLimits.MaximumGrepContextLines}}. Defaults to 0."
                    },
                    "limit": {
                      "type": "integer",
                      "description": "Maximum matching lines in content mode, or files in files and count modes, up to {{WorkspaceLimits.MaximumGrepMatches}}; defaults to {{WorkspaceLimits.DefaultGrepMatches}}."
                    }
                  },
                  "required": ["pattern"]
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

        if (!ToolArguments.TryReadRequiredString(call.Arguments, "pattern", out var pattern)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "path", out var relativePath)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "glob", out var globText)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "output_mode", out var modeText)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "context", out var contextValue)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "limit", out var limitValue))
        {
            return Fail(
                "Arguments must include pattern, with optional path, glob, and output_mode strings "
                + "and context and limit integers.");
        }

        var mode = string.IsNullOrWhiteSpace(modeText) ? ContentMode : modeText.Trim();
        if (mode is not (ContentMode or FilesMode or CountMode))
        {
            return Fail($"output_mode must be {ContentMode}, {FilesMode}, or {CountMode}.");
        }

        if (contextValue is < 0 or > WorkspaceLimits.MaximumGrepContextLines)
        {
            return Fail($"context must be between 0 and {WorkspaceLimits.MaximumGrepContextLines}.");
        }

        if (limitValue is <= 0 or > WorkspaceLimits.MaximumGrepMatches)
        {
            return Fail($"limit must be between 1 and {WorkspaceLimits.MaximumGrepMatches}.");
        }

        var context = contextValue ?? 0;
        var limit = limitValue ?? WorkspaceLimits.DefaultGrepMatches;

        Regex regex;
        try
        {
            regex = new Regex(
                pattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            return Fail("Invalid regular expression: " + exception.Message);
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = null;
        }

        GlobPattern? fileGlob = null;
        if (!string.IsNullOrWhiteSpace(globText)
            && !GlobPattern.TryCreate(globText, out fileGlob, out var globError))
        {
            return Fail(globError);
        }

        if (!TextSearchFiles.TryCollect(_workspace, relativePath, out var files, out var error))
        {
            return Fail(error);
        }

        var builder = new StringBuilder();
        var emitted = 0;
        var stopped = false;
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

            if (!TextSearchFiles.IsSearchable(file))
            {
                continue;
            }

            stopped = mode switch
            {
                FilesMode => AppendFile(file, relative, regex, limit, builder, ref emitted),
                CountMode => AppendCount(file, relative, regex, limit, builder, ref emitted),
                _ => AppendContent(file, relative, regex, context, limit, builder, ref emitted)
            };
            if (stopped)
            {
                break;
            }
        }

        if (emitted == 0)
        {
            return ValueTask.FromResult(new ToolOutput("No matches."));
        }

        var text = builder.ToString().TrimEnd();
        if (text.Length > WorkspaceLimits.MaximumToolOutputCharacters)
        {
            return ValueTask.FromResult(new ToolOutput(ToolOutputText.Truncate(text)));
        }

        if (stopped)
        {
            var unit = mode == ContentMode ? "matches" : "files";
            text += ToolOutputText.LineSeparator + $"[truncated to {limit} {unit}]";
        }

        return ValueTask.FromResult(new ToolOutput(text));
    }

    private static ValueTask<ToolOutput> Fail(string message) =>
        ValueTask.FromResult(new ToolOutput(message, ToolResultStatus.Failure));

    /// <summary>
    /// Prints matching lines with optional context. Returns true when the
    /// search must stop because the limit or the output size was reached.
    /// </summary>
    private static bool AppendContent(
        string fullPath,
        string relative,
        Regex regex,
        int context,
        int limit,
        StringBuilder builder,
        ref int emitted)
    {
        var lines = File.ReadAllLines(fullPath);
        var matched = new List<int>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!IsMatch(regex, lines[index], out var timedOut))
            {
                if (timedOut)
                {
                    break;
                }

                continue;
            }

            matched.Add(index);
        }

        var matchedSet = new HashSet<int>(matched);
        var printedThrough = -1;
        foreach (var index in matched)
        {
            if (emitted >= limit)
            {
                return true;
            }

            var start = Math.Max(index - context, printedThrough + 1);
            var end = Math.Min(index + context, lines.Length - 1);
            var separate = printedThrough >= 0
                ? start > printedThrough + 1
                : builder.Length > 0;
            if (context > 0 && separate && end >= start)
            {
                ToolOutputText.AppendLine(builder, "--");
            }

            for (var line = start; line <= end; line++)
            {
                var marker = matchedSet.Contains(line) ? ':' : '-';
                builder.Append(relative).Append(marker).Append(line + 1).Append(marker);
                ToolOutputText.AppendLine(builder, lines[line]);
            }

            printedThrough = Math.Max(printedThrough, end);
            emitted++;
            if (builder.Length > WorkspaceLimits.MaximumToolOutputCharacters)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AppendFile(
        string fullPath,
        string relative,
        Regex regex,
        int limit,
        StringBuilder builder,
        ref int emitted)
    {
        if (CountMatchingLines(fullPath, regex, stopAtFirst: true) == 0)
        {
            return false;
        }

        if (emitted >= limit)
        {
            return true;
        }

        ToolOutputText.AppendLine(builder, relative);
        emitted++;
        return builder.Length > WorkspaceLimits.MaximumToolOutputCharacters;
    }

    private static bool AppendCount(
        string fullPath,
        string relative,
        Regex regex,
        int limit,
        StringBuilder builder,
        ref int emitted)
    {
        var count = CountMatchingLines(fullPath, regex, stopAtFirst: false);
        if (count == 0)
        {
            return false;
        }

        if (emitted >= limit)
        {
            return true;
        }

        builder.Append(relative).Append(':');
        ToolOutputText.AppendLine(builder, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        emitted++;
        return builder.Length > WorkspaceLimits.MaximumToolOutputCharacters;
    }

    private static int CountMatchingLines(string fullPath, Regex regex, bool stopAtFirst)
    {
        var count = 0;
        foreach (var line in File.ReadLines(fullPath))
        {
            if (!IsMatch(regex, line, out var timedOut))
            {
                if (timedOut)
                {
                    break;
                }

                continue;
            }

            count++;
            if (stopAtFirst)
            {
                break;
            }
        }

        return count;
    }

    private static bool IsMatch(Regex regex, string line, out bool timedOut)
    {
        timedOut = false;
        try
        {
            return regex.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            timedOut = true;
            return false;
        }
    }
}
