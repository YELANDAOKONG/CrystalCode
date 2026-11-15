using System.Text;
using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

internal sealed class MultiReadTool : ITool
{
    public const string ToolName = "multi_read";

    private const int MaximumRanges = 20;

    private const int MaximumRangeCharacters = 20_000;

    private readonly Workspace _workspace;

    public MultiReadTool(Workspace workspace)
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
                    "files": {
                      "type": "array",
                      "minItems": 1,
                      "maxItems": {{MaximumRanges}},
                      "items": {
                        "type": "object",
                        "properties": {
                          "path": {
                            "type": "string",
                            "description": "Workspace-relative or absolute text file path."
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
                    }
                  },
                  "required": ["files"]
                }
                """),
            $"Reads up to {MaximumRanges} text file ranges in one call. "
            + "Repeat a path to read several ranges from the same file. "
            + "Returns numbered lines or an error for each range in request order. "
            + $"Each range is capped at {MaximumRangeCharacters} characters and total output at "
            + $"{WorkspaceLimits.MaximumToolOutputCharacters} characters. "
            + "Paths outside the workspace may require approval unless covered by session read or write access.");
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        var ranges = ReadRanges(call.Arguments);
        if (ranges.Length == 0)
        {
            return ValueTask.FromResult(new ToolOutput(
                $"files must be an array of 1 to {MaximumRanges} read ranges.",
                ToolResultStatus.Failure));
        }

        if (PathsOf(ranges).Any(IsCredentialPath))
        {
            return ValueTask.FromResult(new ToolOutput(
                "Reading credential paths is not allowed.", ToolResultStatus.Failure));
        }

        var builder = new StringBuilder();
        var anySuccess = false;
        for (var index = 0; index < ranges.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = ranges[index].GetRawText();
            ToolArguments.TryReadRequiredString(range, "path", out var path);
            var result = ReadRange(range, cancellationToken);

            if (index > 0)
            {
                builder.Append(ToolOutputText.LineSeparator);
            }

            ToolOutputText.AppendLine(builder, $"[{index + 1}] {path}");
            var succeeded = result.Status == ToolResultStatus.Success;
            anySuccess |= succeeded;
            if (!succeeded)
            {
                builder.Append("Error: ");
            }

            ToolOutputText.AppendLine(builder, result.Text);
            if (builder.Length > WorkspaceLimits.MaximumToolOutputCharacters)
            {
                break;
            }
        }

        return ValueTask.FromResult(new ToolOutput(
            ToolOutputText.Truncate(builder.ToString().TrimEnd()),
            anySuccess ? ToolResultStatus.Success : ToolResultStatus.Failure));
    }

    internal static string[] PathsOf(string arguments) => PathsOf(ReadRanges(arguments));

    private static string[] PathsOf(JsonElement[] ranges) =>
        ranges.Where(static range => range.ValueKind == JsonValueKind.Object
                && range.TryGetProperty("path", out var path)
                && path.ValueKind == JsonValueKind.String)
            .Select(static range => range.GetProperty("path").GetString()!)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

    private static JsonElement[] ReadRanges(string arguments)
    {
        if (!ToolArguments.TryOpen(arguments, out var document))
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array
                || files.GetArrayLength() is < 1 or > MaximumRanges)
            {
                return [];
            }

            return files.EnumerateArray().Select(static range => range.Clone()).ToArray();
        }
    }

    private bool IsCredentialPath(string path) =>
        Workspace.IsCredentialPath(path)
        || (_workspace.TryGetFullPath(path, out var fullPath, out _) && Workspace.IsCredentialPath(fullPath));

    private ToolOutput ReadRange(string range, CancellationToken cancellationToken)
    {
        try
        {
            return ReadTool.Read(_workspace, range, cancellationToken, MaximumRangeCharacters);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ToolOutput("File could not be read.", ToolResultStatus.Failure);
        }
    }
}
