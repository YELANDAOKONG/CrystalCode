using System.Text;

using Crystal.Tools;

namespace CrystalHarness.Tools;

/// <summary>
/// Reads a text file in or outside the workspace. Outside paths require approval.
/// </summary>
public sealed class ReadTool : ITool
{
    internal const string ToolName = "read";

    private const string ToolDescription =
        "Reads a text file. path may be workspace-relative or absolute. "
        + "Paths inside the workspace run without asking. "
        + "Paths outside the workspace require approval. "
        + "Use offset (1-based) and limit for large files. Call in parallel when you need several files. "
        + "Do not read in tiny windows. Use glob when the path is uncertain.";

    private readonly Workspace _workspace;

    public ReadTool(Workspace workspace)
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
                      "description": "Workspace-relative or absolute file path."
                    },
                    "offset": {
                      "type": "integer",
                      "description": "1-based line to start from."
                    },
                    "limit": {
                      "type": "integer",
                      "description": "Maximum number of lines to return."
                    }
                  },
                  "required": ["path"]
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

        if (!ToolArguments.TryReadRequiredString(call.Arguments, "path", out var path)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "offset", out var offset)
            || !ToolArguments.TryReadOptionalInt32(call.Arguments, "limit", out var limit))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include path, with optional offset and limit integers.",
                    ToolResultStatus.Failure));
        }

        if (offset is <= 0 || limit is <= 0)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "offset and limit must be positive when supplied.",
                    ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(path))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Reading credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        if (!_workspace.TryResolveReadableFile(path, out var fullPath, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Reading credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        if (Workspace.LooksBinary(fullPath))
        {
            return ValueTask.FromResult(
                new ToolOutput("File looks binary and will not be read.", ToolResultStatus.Failure));
        }

        var startLine = offset ?? 1;
        var maximumLines = limit ?? WorkspaceLimits.MaximumReadLines;
        var builder = new StringBuilder();
        var lineNumber = 0;
        var written = 0;
        var truncated = false;

        foreach (var line in File.ReadLines(fullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (lineNumber < startLine)
            {
                continue;
            }

            if (written >= maximumLines)
            {
                truncated = true;
                break;
            }

            if (builder.Length + line.Length > WorkspaceLimits.MaximumReadCharacters)
            {
                truncated = true;
                break;
            }

            builder.Append(lineNumber.ToString().PadLeft(6));
            builder.Append('|');
            builder.AppendLine(line);
            written++;
        }

        if (lineNumber == 0)
        {
            return ValueTask.FromResult(new ToolOutput("(empty file)"));
        }

        if (written == 0)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    $"File has {lineNumber} lines; offset {startLine} is past the end.",
                    ToolResultStatus.Failure));
        }

        if (truncated)
        {
            builder.AppendLine(
                $"[truncated after {written} lines; file has at least {lineNumber} lines]");
        }

        return ValueTask.FromResult(new ToolOutput(builder.ToString().TrimEnd()));
    }
}
