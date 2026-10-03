using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Replaces one unique occurrence of text in a file. Paths outside the
/// workspace run only after approval.
/// </summary>
public sealed class EditTool : ITool
{
    public const string ToolName = "edit";

    private const string ToolDescription =
        "Replaces one unique old_string with new_string in an existing file. "
        + "path may be workspace-relative or absolute. "
        + "Paths inside the workspace follow the approval mode. "
        + "Paths outside the workspace require approval: the Review model in Review or Audit, otherwise the operator. "
        + "Read the file first. "
        + "Preserve exact indentation; do not include line-number prefixes. "
        + "The call fails if old_string is missing or appears more than once; add surrounding lines to make it unique. "
        + "Prefer edit over rewriting the whole file with write.";

    private readonly Workspace _workspace;

    public EditTool(Workspace workspace)
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
                    "old_string": {
                      "type": "string",
                      "description": "Exact text that must appear once."
                    },
                    "new_string": {
                      "type": "string",
                      "description": "Replacement text."
                    }
                  },
                  "required": ["path", "old_string", "new_string"]
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

        if (!TryRead(call.Arguments, out var path, out var oldText, out var newText))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include path, old_string, and new_string.",
                    ToolResultStatus.Failure));
        }

        if (oldText.Length == 0)
        {
            return ValueTask.FromResult(
                new ToolOutput("old_string cannot be empty.", ToolResultStatus.Failure));
        }

        if (oldText == newText)
        {
            return ValueTask.FromResult(
                new ToolOutput("old_string and new_string are identical.", ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(path))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Editing credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        if (!_workspace.TryResolveEditableFile(path, out var fullPath, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Editing credential paths is not allowed.",
                    ToolResultStatus.Failure));
        }

        if (Workspace.LooksBinary(fullPath))
        {
            return ValueTask.FromResult(
                new ToolOutput("File looks binary and will not be edited.", ToolResultStatus.Failure));
        }

        var contents = File.ReadAllText(fullPath);
        var count = CountOccurrences(contents, oldText);
        if (count == 0)
        {
            return ValueTask.FromResult(
                new ToolOutput("old_string was not found.", ToolResultStatus.Failure));
        }

        if (count > 1)
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    $"old_string matches {count} times; it must be unique.",
                    ToolResultStatus.Failure));
        }

        var updated = contents.Replace(oldText, newText, StringComparison.Ordinal);
        File.WriteAllText(
            fullPath,
            updated,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var shown = _workspace.Contains(fullPath) ? _workspace.ToRelative(fullPath) : fullPath;
        return ValueTask.FromResult(new ToolOutput($"Edited {shown}."));
    }

    internal static bool TryRead(
        string arguments,
        out string path,
        out string oldText,
        out string newText)
    {
        oldText = string.Empty;
        newText = string.Empty;
        return ToolArguments.TryReadRequiredString(arguments, "path", out path)
            && ToolArguments.TryReadString(arguments, "old_string", out oldText)
            && ToolArguments.TryReadString(arguments, "new_string", out newText);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(value, index, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            count++;
            index = found + value.Length;
        }

        return count;
    }
}
