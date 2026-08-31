using System.Text;

using Crystal.Tools;

namespace CrystalHarness.Tools;

/// <summary>
/// Lists workspace files matching a glob pattern.
/// </summary>
public sealed class GlobTool : ITool
{
    internal const string ToolName = "glob";

    private const string ToolDescription =
        "Lists workspace files matching a glob, for example **/*.cs. "
        + "Optional path limits the search directory. Skips bin, obj, and .git. "
        + "Batch independent searches in parallel.";

    private readonly Workspace _workspace;

    public GlobTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "pattern": {
                      "type": "string",
                      "description": "Glob pattern such as **/*.cs or *.md."
                    },
                    "path": {
                      "type": "string",
                      "description": "Optional workspace-relative directory to search from."
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
            || !ToolArguments.TryReadOptionalString(call.Arguments, "path", out var relativePath))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include pattern and an optional path string.",
                    ToolResultStatus.Failure));
        }

        if (!GlobPattern.TryCreate(pattern, out var glob, out var globError))
        {
            return ValueTask.FromResult(new ToolOutput(globError, ToolResultStatus.Failure));
        }

        var searchRoot = _workspace.Root;
        if (relativePath is not null)
        {
            if (!_workspace.TryResolveExistingLocation(relativePath, out var location, out var error))
            {
                return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
            }

            if (File.Exists(location))
            {
                return ValueTask.FromResult(
                    MatchSingleFile(glob!, location)
                        ? new ToolOutput(_workspace.ToRelative(location))
                        : new ToolOutput("No files matched."));
            }

            searchRoot = location;
        }

        try
        {
            var matches = new List<string>();
            foreach (var file in _workspace.EnumerateFiles(searchRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = _workspace.ToRelative(file);
                if (!glob!.IsMatch(relative)
                    && !glob.IsMatch(Path.GetRelativePath(searchRoot, file).Replace('\\', '/')))
                {
                    continue;
                }

                matches.Add(relative);
                if (matches.Count >= WorkspaceLimits.MaximumGlobMatches)
                {
                    break;
                }
            }

            matches.Sort(StringComparer.Ordinal);
            if (matches.Count == 0)
            {
                return ValueTask.FromResult(new ToolOutput("No files matched."));
            }

            var builder = new StringBuilder();
            foreach (var match in matches)
            {
                builder.AppendLine(match);
            }

            if (matches.Count == WorkspaceLimits.MaximumGlobMatches)
            {
                builder.AppendLine($"[truncated to {WorkspaceLimits.MaximumGlobMatches} files]");
            }

            return ValueTask.FromResult(new ToolOutput(builder.ToString().TrimEnd()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Glob failed: " + exception.Message,
                    ToolResultStatus.Failure));
        }
    }

    private bool MatchSingleFile(GlobPattern glob, string fullPath)
    {
        var relative = _workspace.ToRelative(fullPath);
        return glob.IsMatch(relative) || glob.IsMatch(Path.GetFileName(fullPath));
    }
}
