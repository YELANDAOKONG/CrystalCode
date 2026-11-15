using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Reads a text file in or outside the workspace. Outside paths require approval.
/// </summary>
public sealed class ReadTool : ITool
{
    public const string ToolName = "read";

    private const string ToolDescription =
        "Reads a text file. path may be workspace-relative or absolute. "
        + "Paths inside the workspace run without asking. "
        + "Paths outside the workspace may require approval unless covered by session read or write access. "
        + "Use offset (1-based) and limit for large files. Use multi_read when you need several files or line ranges. "
        + "Do not read in tiny windows. Use glob when the path is uncertain.";

    private readonly Workspace _workspace;

    public ReadTool(Workspace workspace)
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

        return ValueTask.FromResult(Read(_workspace, call.Arguments, cancellationToken));
    }

    internal static ToolOutput Read(
        Workspace workspace,
        string arguments,
        CancellationToken cancellationToken,
        int maximumCharacters = WorkspaceLimits.MaximumReadCharacters)
    {
        if (!ToolArguments.TryReadRequiredString(arguments, "path", out var path)
            || !ToolArguments.TryReadOptionalInt32(arguments, "offset", out var offset)
            || !ToolArguments.TryReadOptionalInt32(arguments, "limit", out var limit))
        {
            return new ToolOutput(
                "Arguments must include path, with optional offset and limit integers.",
                ToolResultStatus.Failure);
        }

        if (offset is <= 0 || limit is <= 0)
        {
            return new ToolOutput(
                "offset and limit must be positive when supplied.",
                ToolResultStatus.Failure);
        }

        if (Workspace.IsCredentialPath(path))
        {
            return new ToolOutput(
                "Reading credential paths is not allowed.",
                ToolResultStatus.Failure);
        }

        if (!workspace.TryResolveReadableFile(path, out var fullPath, out var error))
        {
            return new ToolOutput(error, ToolResultStatus.Failure);
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            return new ToolOutput(
                "Reading credential paths is not allowed.",
                ToolResultStatus.Failure);
        }

        if (Workspace.LooksBinary(fullPath))
        {
            return new ToolOutput("File looks binary and will not be read.", ToolResultStatus.Failure);
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

            if (builder.Length + line.Length > maximumCharacters)
            {
                truncated = true;
                break;
            }

            builder.Append(lineNumber.ToString().PadLeft(6));
            builder.Append('|');
            ToolOutputText.AppendLine(builder, line);
            written++;
        }

        if (lineNumber == 0)
        {
            return new ToolOutput("(empty file)");
        }

        if (written == 0 && !truncated)
        {
            return new ToolOutput(
                $"File has {lineNumber} lines; offset {startLine} is past the end.",
                ToolResultStatus.Failure);
        }

        if (truncated)
        {
            ToolOutputText.AppendLine(
                builder,
                $"[truncated after {written} lines; file has at least {lineNumber} lines]");
        }

        return new ToolOutput(builder.ToString().TrimEnd());
    }
}
