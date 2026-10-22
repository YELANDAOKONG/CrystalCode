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

    private const string ToolDescription =
        "Searches text files under the requested path with a .NET regular expression. "
        + "Optional path may be workspace-relative or absolute and defaults to the workspace root; an optional file-name glob narrows the search. "
        + "Session read or write access permits searching an outside directory; unlisted outside paths require approval outside Full mode. "
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
                """
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
            || !ToolArguments.TryReadOptionalString(call.Arguments, "glob", out var globText))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include pattern, with optional path and glob strings.",
                    ToolResultStatus.Failure));
        }

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

        var builder = new StringBuilder();
        var matches = 0;
        var truncated = false;
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

            if (!TrySearchFile(file, relative, regex, builder, ref matches, out truncated))
            {
                continue;
            }

            if (truncated)
            {
                break;
            }
        }

        if (matches == 0)
        {
            return ValueTask.FromResult(new ToolOutput("No matches."));
        }

        if (truncated)
        {
            ToolOutputText.AppendLine(
                builder,
                $"[truncated to {WorkspaceLimits.MaximumGrepMatches} matches]");
        }

        return ValueTask.FromResult(new ToolOutput(builder.ToString().TrimEnd()));
    }

    private static bool TrySearchFile(
        string fullPath,
        string relative,
        Regex regex,
        StringBuilder builder,
        ref int matches,
        out bool truncated)
    {
        truncated = false;
        if (!TextSearchFiles.IsSearchable(fullPath))
        {
            return false;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(fullPath))
        {
            lineNumber++;
            bool found;
            try
            {
                found = regex.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }

            if (!found)
            {
                continue;
            }

            builder.Append(relative);
            builder.Append(':');
            builder.Append(lineNumber);
            builder.Append(':');
            ToolOutputText.AppendLine(builder, line);
            matches++;
            if (matches >= WorkspaceLimits.MaximumGrepMatches)
            {
                truncated = true;
                return true;
            }
        }

        return true;
    }
}
