using System.Text;

using Crystal.Tools;

namespace CrystalHarness.Tools;

/// <summary>
/// Replaces one unique occurrence of text in a workspace file.
/// </summary>
public sealed class EditTool : ITool
{
    internal const string ToolName = "edit";

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
                      "description": "Workspace-relative file path."
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
            "Replaces one unique occurrence of old_string after the user approves.");
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

        if (!_workspace.TryResolveExistingFile(path, out var fullPath, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
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
        return ValueTask.FromResult(
            new ToolOutput($"Edited {_workspace.ToRelative(fullPath)}."));
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
