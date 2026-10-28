using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Lists the directory structure under a path. Within each directory,
/// subdirectories come before files, and the walk is bounded by depth, entry,
/// and per-directory file caps.
/// </summary>
public sealed class TreeTool : ITool
{
    public const string ToolName = "tree";

    private static readonly string ToolDescription =
        "Lists the directory structure under a path, one workspace-relative path per line, depth-first. "
        + "Subdirectories come before files within each directory, each group is sorted by entry name, "
        + "and directories end with a slash. "
        + "Use it to see the shape of an unfamiliar directory; use list for one directory's immediate entries, "
        + "glob to find files by name, and search to find candidate files by name and content. "
        + "Optional path defaults to the workspace root; outside paths may require approval unless covered by session read or write access. "
        + $"Optional depth controls how many levels below path are listed (default {WorkspaceLimits.DefaultTreeDepth}, "
        + $"capped at {WorkspaceLimits.MaximumTreeDepth}). "
        + $"Set files true to include file entries; each directory lists at most "
        + $"{WorkspaceLimits.MaximumTreeFilesPerDirectory} files and notes the rest. "
        + "Skips .git, .vs, bin, obj, node_modules, dist, and credential paths. "
        + "A directory link is listed once, under its resolved path; a link whose target leaves "
        + "the workspace root (or an outside start path) is skipped. "
        + $"Output stops at {WorkspaceLimits.MaximumTreeEntries} entries with a truncation note; "
        + "narrow the path or reduce depth instead of paging.";

    private readonly Workspace _workspace;

    public TreeTool(Workspace workspace)
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
                    "path": {
                      "type": "string",
                      "description": "Optional workspace-relative or absolute directory. Defaults to the workspace root."
                    },
                    "depth": {
                      "type": "integer",
                      "description": "How many levels below path to list. Defaults to {{WorkspaceLimits.DefaultTreeDepth}}; values above {{WorkspaceLimits.MaximumTreeDepth}} are capped."
                    },
                    "files": {
                      "type": "boolean",
                      "description": "Include file entries. Defaults to false (directories only). Each directory lists at most {{WorkspaceLimits.MaximumTreeFilesPerDirectory}} files."
                    }
                  }
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

        if (!ToolArguments.IsObjectOrEmpty(call.Arguments)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "path", out var path)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "depth", out var depth)
            || !ToolArguments.TryReadOptionalBoolean(call.Arguments, "files", out var files))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must be a JSON object with optional path string, depth integer, and files boolean.",
                    ToolResultStatus.Failure));
        }

        if (depth is <= 0)
        {
            return ValueTask.FromResult(
                new ToolOutput("depth must be positive when supplied.", ToolResultStatus.Failure));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = null;
        }

        if (path is not null && Workspace.IsCredentialPath(path))
        {
            return ValueTask.FromResult(
                new ToolOutput("Listing credential paths is not allowed.", ToolResultStatus.Failure));
        }

        var requestedPath = path ?? ".";
        if (!_workspace.TryResolveReadableLocation(requestedPath, out var location, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(location))
        {
            return ValueTask.FromResult(
                new ToolOutput("Listing credential paths is not allowed.", ToolResultStatus.Failure));
        }

        try
        {
            if (File.Exists(location))
            {
                return ValueTask.FromResult(new ToolOutput(_workspace.ToRelative(location)));
            }

            var entries = EnumerateEntries(location, depth, files == true, cancellationToken);
            if (entries.Length == 0)
            {
                return ValueTask.FromResult(
                    new ToolOutput(files == true ? "(empty directory)" : "(no directories)"));
            }

            return ValueTask.FromResult(new ToolOutput(entries));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(
                new ToolOutput("Tree failed: " + exception.Message, ToolResultStatus.Failure));
        }
    }

    private string EnumerateEntries(
        string location,
        int? depth,
        bool includeFiles,
        CancellationToken cancellationToken)
    {
        var maximumDepth = Math.Min(
            depth ?? WorkspaceLimits.DefaultTreeDepth,
            WorkspaceLimits.MaximumTreeDepth);
        var builder = new StringBuilder();
        var entryCount = 0;
        var truncated = false;
        var fileParent = string.Empty;
        var filesInParent = 0;
        var skippedInParent = 0;

        void FlushSkippedFiles()
        {
            if (skippedInParent == 0)
            {
                return;
            }

            // ToRelative reports the workspace root as ".", which would read
            // as "[+N more files in .]".
            var relative = _workspace.ToRelative(fileParent);
            var shown = relative == "." ? "the workspace root" : relative;
            ToolOutputText.AppendLine(
                builder,
                $"[+{skippedInParent} more files in {shown}]");
            skippedInParent = 0;
        }

        foreach (var entry in _workspace.EnumerateTree(location, maximumDepth, includeFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                FlushSkippedFiles();
                ToolOutputText.AppendLine(builder, _workspace.ToRelative(entry.Path) + "/");
                entryCount++;
            }
            else
            {
                var parent = Path.GetDirectoryName(entry.Path) ?? string.Empty;
                if (!string.Equals(parent, fileParent, StringComparison.Ordinal))
                {
                    FlushSkippedFiles();
                    fileParent = parent;
                    filesInParent = 0;
                }

                if (filesInParent < WorkspaceLimits.MaximumTreeFilesPerDirectory)
                {
                    ToolOutputText.AppendLine(builder, _workspace.ToRelative(entry.Path));
                    filesInParent++;
                    entryCount++;
                }
                else
                {
                    skippedInParent++;
                }
            }

            if (entryCount >= WorkspaceLimits.MaximumTreeEntries)
            {
                truncated = true;
                break;
            }
        }

        FlushSkippedFiles();
        if (truncated)
        {
            ToolOutputText.AppendLine(
                builder,
                $"[truncated after {WorkspaceLimits.MaximumTreeEntries} entries; narrow the path or reduce depth]");
        }

        return builder.ToString().TrimEnd(ToolOutputText.LineSeparator);
    }
}
