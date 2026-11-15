using System.Text;
using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Prepares exact text replacements across existing files before writing any file.
/// </summary>
public sealed class MultiEditTool : ITool
{
    public const string ToolName = "multi_edit";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: true,
        throwOnInvalidBytes: true);

    private readonly Workspace _workspace;

    public MultiEditTool(Workspace workspace)
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
                    "continue_on_error": {
                      "type": "boolean",
                      "default": false,
                      "description": "Skip files with preflight or write errors and continue editing other files."
                    },
                    "files": {
                      "type": "array",
                      "minItems": 1,
                      "items": {
                        "type": "object",
                        "properties": {
                          "path": { "type": "string", "description": "Workspace-relative or absolute path to an existing file." },
                          "edits": {
                            "type": "array",
                            "minItems": 1,
                            "items": {
                              "type": "object",
                              "properties": {
                                "old_string": { "type": "string", "description": "Nonempty exact text to replace." },
                                "new_string": { "type": "string", "description": "Replacement text. Use an empty string to delete." },
                                "replace_all": { "type": "boolean", "description": "Replace all matches. Defaults to false." }
                              },
                              "required": ["old_string", "new_string"]
                            }
                          }
                        },
                        "required": ["path", "edits"]
                      }
                    }
                  },
                  "required": ["files"]
                }
                """),
            "Applies multiple exact text replacements to one or more existing UTF-8 files. "
            + "Read each file first and list it once. Edits run in order within each file. "
            + "Each old_string must match exactly once unless replace_all is true. "
            + "By default, a file error stops the batch. With continue_on_error, file errors are reported and other files still run. "
            + "Batch validation and access checks still apply before writing. "
            + "Unlisted outside paths may require approval. Credential paths are refused.");
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryPrepare(_workspace, call.Arguments, cancellationToken, out var preparation, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        var failures = new List<string>(preparation.Failures);
        var results = new List<string>(preparation.Files.Count);
        foreach (var file in preparation.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = file.Change.Path;
            var shown = _workspace.Contains(path) ? _workspace.ToRelative(path) : path;
            var writeError = WriteError(file);
            if (writeError is not null)
            {
                if (!preparation.ContinueOnError)
                {
                    return ValueTask.FromResult(PartialFailure(shown, writeError, results));
                }

                failures.Add($"{shown}: {writeError}");
                continue;
            }

            results.Add($"Edited {shown}. Replacements: {file.ReplacementCount}.");
        }

        if (failures.Count > 0)
        {
            var summary = $"Edited files: {results.Count}. Failed files: {failures.Count}.";
            var report = string.Join(ToolOutputText.LineSeparator, new[] { summary }.Concat(failures).Concat(results));
            return ValueTask.FromResult(new ToolOutput(ToolOutputText.Truncate(report), ToolResultStatus.Failure));
        }

        return ValueTask.FromResult(new ToolOutput(
            ToolOutputText.Truncate(string.Join(ToolOutputText.LineSeparator, results))));
    }

    internal static IReadOnlyList<string> PathsOf(string arguments) =>
        TryRead(arguments, out var files, out _, out _) ? files.Select(static file => file.Path).ToArray() : [];

    internal static IReadOnlyList<ToolFileChange> PreviewChanges(Workspace workspace, string arguments) =>
        TryPrepare(workspace, arguments, default, out var preparation, out _)
            ? [.. preparation.Files.Select(static file => file.Change)]
            : [];

    /// <summary>
    /// Lists the requested replacements for a front end that shows an approval preview.
    /// </summary>
    public static IEnumerable<(string Path, string OldText, string NewText, bool ReplaceAll)> PreviewEdits(string arguments)
    {
        if (!TryRead(arguments, out var files, out _, out _))
        {
            yield break;
        }

        foreach (var file in files)
        {
            foreach (var edit in file.Edits)
            {
                yield return (file.Path, edit.OldText, edit.NewText, edit.ReplaceAll);
            }
        }
    }

    private static bool TryPrepare(
        Workspace workspace,
        string arguments,
        CancellationToken cancellationToken,
        out MultiEditPreparation preparation,
        out string error)
    {
        preparation = null!;
        if (!TryRead(arguments, out var files, out var continueOnError, out error))
        {
            return false;
        }

        var pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(pathComparer);
        var prepared = new List<PreparedMultiEdit>();
        var failures = new List<string>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Workspace.IsCredentialPath(file.Path))
            {
                error = "Editing credential paths is not allowed.";
                return false;
            }

            if (!workspace.TryGetFullPath(file.Path, out var fullPath, out var problem))
            {
                error = $"{file.Path}: {problem}";
                if (!continueOnError)
                {
                    return false;
                }

                failures.Add(error);
                continue;
            }

            if (Workspace.IsCredentialPath(fullPath))
            {
                error = "Editing credential paths is not allowed.";
                return false;
            }

            if (workspace.IsReadOnly(fullPath))
            {
                error = $"{file.Path}: Writing to a read-only session directory is not allowed. Change its access with /access.";
                return false;
            }

            if (!seen.Add(fullPath))
            {
                error = $"File {file.Path} is listed more than once. Group its edits under one path.";
                return false;
            }

            if (!TryPrepareFile(workspace, file, fullPath, cancellationToken, out var change, out error))
            {
                if (!continueOnError)
                {
                    return false;
                }

                failures.Add(error);
                continue;
            }

            prepared.Add(change);
        }

        preparation = new MultiEditPreparation(continueOnError, prepared, failures);
        error = string.Empty;
        return true;
    }

    private static bool TryRead(
        string arguments, out List<MultiEditFile> files, out bool continueOnError, out string error)
    {
        files = [];
        continueOnError = false;
        error = "Arguments must include a nonempty files array.";
        if (!ToolArguments.TryOpen(arguments, out var document))
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.TryGetProperty("continue_on_error", out var flag))
            {
                if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "continue_on_error must be a boolean.";
                    return false;
                }

                continueOnError = flag.GetBoolean();
            }

            if (!TryReadArray(document.RootElement, "files", out var fileEntries))
            {
                return false;
            }

            foreach (var entry in fileEntries.EnumerateArray())
            {
                if (!TryReadString(entry, "path", out var path) || string.IsNullOrWhiteSpace(path))
                {
                    error = $"File entry {files.Count + 1} must include a path.";
                    return false;
                }

                if (!TryReadArray(entry, "edits", out var editEntries))
                {
                    error = $"File {path} must include a nonempty edits array.";
                    return false;
                }

                var edits = new List<TextReplacement>();
                foreach (var editEntry in editEntries.EnumerateArray())
                {
                    if (!TryReadEdit(editEntry, out var edit, out var problem))
                    {
                        error = $"{path}, edit {edits.Count + 1}: {problem}";
                        return false;
                    }

                    edits.Add(edit);
                }

                files.Add(new MultiEditFile(path, edits));
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadEdit(JsonElement entry, out TextReplacement edit, out string error)
    {
        edit = null!;
        if (!TryReadString(entry, "old_string", out var oldText)
            || oldText.Length == 0
            || !TryReadString(entry, "new_string", out var newText))
        {
            error = "Each edit needs a nonempty old_string and a new_string.";
            return false;
        }

        if (oldText == newText)
        {
            error = "old_string and new_string are identical.";
            return false;
        }

        var replaceAll = false;
        if (entry.TryGetProperty("replace_all", out var flag))
        {
            if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
            {
                error = "replace_all must be a boolean.";
                return false;
            }

            replaceAll = flag.ValueKind == JsonValueKind.True;
        }

        edit = new TextReplacement(oldText, newText, replaceAll);
        error = string.Empty;
        return true;
    }

    private static bool TryReadArray(JsonElement entry, string name, out JsonElement value) =>
        entry.TryGetProperty(name, out value)
        && value.ValueKind == JsonValueKind.Array
        && value.GetArrayLength() > 0;

    private static bool TryReadString(JsonElement entry, string name, out string value)
    {
        value = string.Empty;
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            value = property.GetString()!;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryPrepareFile(
        Workspace workspace,
        MultiEditFile file,
        string fullPath,
        CancellationToken cancellationToken,
        out PreparedMultiEdit prepared,
        out string error)
    {
        prepared = null!;
        if (!workspace.TryRequireExistingFile(fullPath, out var pathError))
        {
            error = $"{file.Path}: {pathError}";
            return false;
        }

        byte[] bytes;
        string original;
        bool hasByteOrderMark;
        try
        {
            using var stream = File.OpenRead(fullPath);
            if (stream.Length > WorkspaceLimits.MaximumWriteBytes)
            {
                error = $"{file.Path} exceeds {WorkspaceLimits.MaximumWriteBytes} bytes.";
                return false;
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            if (bytes.AsSpan().Contains((byte)0))
            {
                error = $"{file.Path} looks binary and will not be edited.";
                return false;
            }

            hasByteOrderMark = bytes.AsSpan().StartsWith(StrictUtf8.Preamble);
            original = StrictUtf8.GetString(bytes.AsSpan(hasByteOrderMark ? StrictUtf8.Preamble.Length : 0));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            error = $"{file.Path} could not be read as UTF-8 text.";
            return false;
        }

        var updated = original;
        var total = 0L;
        var limit = WorkspaceLimits.MaximumWriteBytes - (hasByteOrderMark ? StrictUtf8.Preamble.Length : 0);
        for (var index = 0; index < file.Edits.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edit = file.Edits[index];
            if (!EditTool.TryReplace(updated, edit.OldText, edit.NewText, edit.ReplaceAll,
                    out updated, out var count, out var problem, limit))
            {
                error = $"{file.Path}, edit {index + 1}: {problem}";
                return false;
            }

            total += count;
        }

        var change = new ToolFileChange(fullPath, original, updated);
        prepared = new PreparedMultiEdit(change, bytes, hasByteOrderMark, total);
        error = string.Empty;
        return true;
    }

    private static string? WriteError(PreparedMultiEdit file)
    {
        try
        {
            return TryWrite(file) ? null : "The file changed after preflight.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "The write could not be completed. Read the file again before retrying.";
        }
    }

    private static bool TryWrite(PreparedMultiEdit file)
    {
        var bytes = StrictUtf8.GetBytes(file.Change.NewText);
        // Verify and write through the same exclusive handle so concurrent
        // callers cannot replace a file based on an already stale snapshot.
        using var stream = new FileStream(file.Change.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (stream.Length != file.OriginalBytes.Length)
        {
            return false;
        }

        var current = new byte[file.OriginalBytes.Length];
        stream.ReadExactly(current);
        if (!current.AsSpan().SequenceEqual(file.OriginalBytes))
        {
            return false;
        }

        if (file.Change.OldText == file.Change.NewText)
        {
            return true;
        }

        stream.Position = 0;
        if (file.HasByteOrderMark)
        {
            stream.Write(StrictUtf8.Preamble);
        }

        stream.Write(bytes);
        stream.SetLength(stream.Position);
        return true;
    }

    private static ToolOutput PartialFailure(string path, string error, List<string> results) =>
        new(ToolOutputText.Truncate(
            $"Stopped at {path}. {error}\n"
            + (results.Count == 0 ? "No earlier file was edited." : string.Join(ToolOutputText.LineSeparator, results))),
            ToolResultStatus.Failure);
}
