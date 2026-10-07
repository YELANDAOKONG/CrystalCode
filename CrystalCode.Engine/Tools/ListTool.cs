using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Lists the immediate entries of a directory in or outside the workspace.
/// Outside paths require approval.
/// </summary>
public sealed class ListTool : ITool
{
    public const string ToolName = "list";

    private readonly Workspace _workspace;

    public ListTool(Workspace workspace)
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
                    "path": {
                      "type": "string",
                      "description": "Optional workspace-relative or absolute directory path. Defaults to the workspace root."
                    },
                    "offset": {
                      "type": "integer",
                      "description": "1-based entry to start from."
                    },
                    "limit": {
                      "type": "integer",
                      "description": "Maximum number of entries to return, up to 1000."
                    }
                  }
                }
                """),
            Describe());
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        // Every field is optional, so a malformed payload would otherwise
        // silently degrade to an argument-less workspace listing.
        if (!ToolArguments.IsObjectOrEmpty(call.Arguments))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must be a JSON object with optional path, offset, and limit.",
                    ToolResultStatus.Failure));
        }

        if (!ToolArguments.TryReadOptionalString(call.Arguments, "path", out var path)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "offset", out var offset)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "limit", out var limit))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include an optional path with optional offset and limit integers.",
                    ToolResultStatus.Failure));
        }

        if (offset is <= 0 || limit is <= 0)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "offset and limit must be positive when supplied.",
                    ToolResultStatus.Failure));
        }

        if (path is not null && Workspace.IsCredentialPath(path))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Listing credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        var requestedPath = path ?? ".";
        if (!_workspace.TryResolveReadableLocation(requestedPath, out var location, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(location))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Listing credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        try
        {
            var isFile = File.Exists(location);
            var entries = ReadEntries(location, isFile, cancellationToken);
            entries.Sort(StringComparer.Ordinal);
            if (entries.Count == 0)
            {
                return ValueTask.FromResult(new ToolOutput("(empty directory)"));
            }

            var start = offset ?? 1;
            if (start > entries.Count)
            {
                var subject = isFile ? "File" : "Directory";
                var noun = entries.Count == 1 ? "entry" : "entries";
                return ValueTask.FromResult(
                    new ToolOutput(
                        $"{subject} has {entries.Count} {noun}; offset {start} is past the end.",
                        ToolResultStatus.Failure));
            }

            var maximum = Math.Min(
                limit ?? WorkspaceLimits.MaximumListEntries,
                WorkspaceLimits.MaximumListEntries);
            var count = Math.Min(maximum, entries.Count - start + 1);
            var builder = new StringBuilder();
            for (var index = start - 1; index < start - 1 + count; index++)
            {
                builder.AppendLine(entries[index]);
            }

            if (start - 1 + count < entries.Count)
            {
                builder.AppendLine(
                    $"[showing {count} of {entries.Count} entries; continue with offset {start + count}]");
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
                    "List failed: " + exception.Message,
                    ToolResultStatus.Failure));
        }
    }

    private static string Describe() =>
        "Lists the immediate entries of a directory. path may be workspace-relative or absolute "
        + "and defaults to the workspace root. Directories end with a slash. "
        + "Skips bin, obj, .git, .vs, node_modules, and dist. "
        + "Use offset (1-based) and limit to page through large directories; "
        + $"limit defaults to {WorkspaceLimits.MaximumListEntries} entries and is capped there. "
        + "Paths outside the workspace require approval.";

    private List<string> ReadEntries(string location, bool isFile, CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        if (isFile)
        {
            entries.Add(_workspace.ToRelative(location));
            return entries;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(location))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = Directory.Exists(entry);
            var name = Path.GetFileName(entry);
            if ((isDirectory && _workspace.IsIgnoredDirectoryName(name))
                || Workspace.IsCredentialPath(entry))
            {
                continue;
            }

            var display = _workspace.ToRelative(entry);
            entries.Add(isDirectory ? display + "/" : display);
        }

        return entries;
    }
}
