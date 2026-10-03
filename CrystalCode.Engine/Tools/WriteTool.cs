using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Creates or overwrites a text file. Paths outside the workspace run only
/// after approval.
/// </summary>
public sealed class WriteTool : ITool
{
    public const string ToolName = "write";

    private const string ToolDescription =
        "Creates or overwrites a text file. path may be workspace-relative or absolute. "
        + "Paths inside the workspace follow the approval mode. "
        + "Paths outside the workspace require approval: the Review model in Review or Audit, otherwise the operator. "
        + "Read an existing file first. "
        + "Prefer edit; use write only to create a file or replace the whole file. "
        + "Do not create a README or other documentation unless the user asked.";

    private readonly Workspace _workspace;

    public WriteTool(Workspace workspace)
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
                      "description": "Workspace-relative or absolute file path to create or overwrite."
                    },
                    "contents": {
                      "type": "string",
                      "description": "Exact UTF-8 file contents."
                    }
                  },
                  "required": ["path", "contents"]
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

        if (!TryRead(call.Arguments, out var path, out var contents))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include path and contents strings.",
                    ToolResultStatus.Failure));
        }

        if (Encoding.UTF8.GetByteCount(contents) > WorkspaceLimits.MaximumWriteBytes)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    $"Contents exceed {WorkspaceLimits.MaximumWriteBytes} bytes.",
                    ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(path))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Writing credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        if (!_workspace.TryResolveOutputPath(path, out var fullPath, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Writing credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var existed = File.Exists(fullPath);
        File.WriteAllText(fullPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var shown = _workspace.Contains(fullPath) ? _workspace.ToRelative(fullPath) : fullPath;
        var action = existed ? "Wrote" : "Created";
        return ValueTask.FromResult(new ToolOutput($"{action} {shown} ({contents.Length} characters)."));
    }

    internal static bool TryRead(string arguments, out string path, out string contents)
    {
        contents = string.Empty;
        return ToolArguments.TryReadRequiredString(arguments, "path", out path)
            && ToolArguments.TryReadString(arguments, "contents", out contents);
    }
}
