using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Lists workspace files matching a glob pattern.
/// </summary>
public sealed class GlobTool : ITool
{
    public const string ToolName = "glob";

    private static readonly string ToolDescription =
        "Lists workspace files matching a glob, for example **/*.cs. "
        + "Optional path limits the search directory. Skips .git, .vs, bin, obj, node_modules, and dist. "
        + "Use offset (1-based) and limit to page through matches; "
        + $"limit defaults to {WorkspaceLimits.MaximumGlobMatches} files and is capped there. "
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
                    },
                    "offset": {
                      "type": "integer",
                      "description": "1-based match to start from."
                    },
                    "limit": {
                      "type": "integer",
                      "description": "Maximum number of matches to return, up to 1000."
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
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "offset", out var offset)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "limit", out var limit))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include pattern, with optional path string and offset and limit integers.",
                    ToolResultStatus.Failure));
        }

        if (offset is <= 0 || limit is <= 0)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "offset and limit must be positive when supplied.",
                    ToolResultStatus.Failure));
        }

        if (!GlobPattern.TryCreate(pattern, out var glob, out var globError))
        {
            return ValueTask.FromResult(new ToolOutput(globError, ToolResultStatus.Failure));
        }

        var searchRoot = _workspace.Root;
        if (relativePath is not null)
        {
            if (Workspace.IsCredentialPath(relativePath))
            {
                return ValueTask.FromResult(
                    new ToolOutput(
                        "Searching credential paths is not allowed.",
                        ToolResultStatus.Failure));
            }

            if (!_workspace.TryResolveReadableLocation(relativePath, out var location, out var error))
            {
                return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
            }

            if (Workspace.IsCredentialPath(location))
            {
                return ValueTask.FromResult(
                    new ToolOutput(
                        "Searching credential paths is not allowed.",
                        ToolResultStatus.Failure));
            }

            searchRoot = location;
        }

        try
        {
            var start = offset ?? 1;
            var maximum = Math.Min(
                limit ?? WorkspaceLimits.MaximumGlobMatches,
                WorkspaceLimits.MaximumGlobMatches);
            var keep = start - 1L + maximum;
            var total = 0;
            List<string> selected;
            if (File.Exists(searchRoot))
            {
                selected = [];
                if (MatchSingleFile(glob!, searchRoot))
                {
                    total = 1;
                    selected.Add(_workspace.ToRelative(searchRoot));
                }
            }
            else
            {
                // Keep only the smallest keep matches so memory stays bounded
                // while pages stay consistent with the full ordinal order.
                var smallest = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var file in _workspace.EnumerateFiles(searchRoot))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = _workspace.ToRelative(file);
                    if (Workspace.IsCredentialPath(file) || Workspace.IsCredentialPath(relative))
                    {
                        continue;
                    }

                    if (!glob!.IsMatch(relative)
                        && !glob.IsMatch(Path.GetRelativePath(searchRoot, file).Replace('\\', '/')))
                    {
                        continue;
                    }

                    if (smallest.Add(relative))
                    {
                        total++;
                        if (smallest.Count > keep)
                        {
                            smallest.Remove(smallest.Max!);
                        }
                    }
                }

                selected = [.. smallest];
            }

            if (total == 0)
            {
                return ValueTask.FromResult(new ToolOutput("No files matched."));
            }

            if (start > total)
            {
                var noun = total == 1 ? "file" : "files";
                return ValueTask.FromResult(
                    new ToolOutput(
                        $"Glob matched {total} {noun}; offset {start} is past the end.",
                        ToolResultStatus.Failure));
            }

            var count = Math.Min(maximum, total - start + 1);
            var builder = new StringBuilder();
            for (var index = start - 1; index < start - 1 + count; index++)
            {
                builder.AppendLine(selected[index]);
            }

            if (start - 1 + count < total)
            {
                builder.AppendLine(
                    $"[showing {count} of {total} files; continue with offset {start + count}]");
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
