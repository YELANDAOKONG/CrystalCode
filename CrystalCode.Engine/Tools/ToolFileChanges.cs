using System.Text;

using Crystal.Tools;

using CrystalCode.Engine.Tools.Patches;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Computes the files a proposed file call would change, from the bytes on
/// disk now, without writing anything. Paths resolve against the workspace
/// root the same way the tools do. Credential paths, binary files, files over
/// the write limit, and calls that would fail are left out, so a front end
/// shows nothing rather than a misleading diff.
/// </summary>
public static class ToolFileChanges
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<ToolFileChange> Preview(string workspaceRoot, ToolCall call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(call);
        Workspace workspace;
        try
        {
            workspace = new Workspace(workspaceRoot);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return [];
        }

        return call.Name switch
        {
            EditTool.ToolName => Edit(workspace, call.Arguments),
            WriteTool.ToolName => Write(workspace, call.Arguments),
            ApplyPatchTool.ToolName => Patch(workspace, call.Arguments),
            _ => []
        };
    }

    /// <summary>
    /// Absolute paths named by a call's <c>path</c> or <c>filePath</c>
    /// argument, or by every section of a patch.
    /// </summary>
    public static IReadOnlyList<string> Paths(string workspaceRoot, ToolCall call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(call);
        Workspace workspace;
        try
        {
            workspace = new Workspace(workspaceRoot);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return [];
        }

        IEnumerable<string> named = call.Name == ApplyPatchTool.ToolName
            ? ApplyPatchTool.PathsOf(call.Arguments)
            : ToolArguments.TryReadRequiredString(call.Arguments, "path", out var path)
                ? [path]
                : ToolArguments.TryReadRequiredString(call.Arguments, "filePath", out var filePath)
                    ? [filePath]
                    : [];
        var resolved = new List<string>();
        foreach (var value in named)
        {
            if (workspace.TryGetFullPath(value, out var full, out _)
                && !resolved.Contains(full, StringComparer.Ordinal))
            {
                resolved.Add(full);
            }
        }

        return resolved;
    }

    private static IReadOnlyList<ToolFileChange> Edit(Workspace workspace, string arguments)
    {
        if (!EditTool.TryRead(arguments, out var path, out var oldText, out var newText)
            || oldText.Length == 0
            || !TryResolve(workspace, path, out var full)
            || !TryReadText(full, out var current))
        {
            return [];
        }

        var replaceAll = ToolArguments.TryReadOptionalBoolean(arguments, "replace_all", out var all) && all == true;
        var first = current.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0)
        {
            return [];
        }

        var updated = replaceAll
            ? current.Replace(oldText, newText, StringComparison.Ordinal)
            : string.Concat(current.AsSpan(0, first), newText, current.AsSpan(first + oldText.Length));
        return [new ToolFileChange(full, current, updated)];
    }

    private static IReadOnlyList<ToolFileChange> Write(Workspace workspace, string arguments)
    {
        if (!ToolArguments.TryReadRequiredString(arguments, "path", out var path)
            || !ToolArguments.TryReadString(arguments, "contents", out var contents)
            || !TryResolve(workspace, path, out var full))
        {
            return [];
        }

        if (!File.Exists(full))
        {
            return [new ToolFileChange(full, null, contents)];
        }

        return TryReadText(full, out var current) ? [new ToolFileChange(full, current, contents)] : [];
    }

    private static IReadOnlyList<ToolFileChange> Patch(Workspace workspace, string arguments)
    {
        if (!ApplyPatchTool.TryReadPatch(arguments, out var patchText)
            || !PatchParser.TryParse(patchText, out var operations, out _))
        {
            return [];
        }

        var changes = new List<ToolFileChange>(operations.Count);
        foreach (var operation in operations)
        {
            if (!TryResolve(workspace, operation.Path, out var full))
            {
                continue;
            }

            switch (operation)
            {
                case PatchAddition addition:
                    changes.Add(new ToolFileChange(
                        full,
                        null,
                        addition.Lines.Count == 0 ? string.Empty : string.Join('\n', addition.Lines) + "\n"));
                    break;
                case PatchDeletion:
                    if (TryReadText(full, out var deleted))
                    {
                        changes.Add(new ToolFileChange(full, deleted, string.Empty, Delete: true));
                    }

                    break;
                case PatchUpdate update:
                    if (!TryReadText(full, out var original)
                        || !ChunkApplier.TryApply(original, update.Chunks, out var updated, out _, out _))
                    {
                        break;
                    }

                    if (update.MovePath is not null && TryResolve(workspace, update.MovePath, out var target))
                    {
                        changes.Add(new ToolFileChange(target, original, updated, MoveFrom: full));
                    }
                    else
                    {
                        changes.Add(new ToolFileChange(full, original, updated));
                    }

                    break;
                default:
                    break;
            }
        }

        return changes;
    }

    private static bool TryResolve(Workspace workspace, string path, out string full)
    {
        full = string.Empty;
        return !string.IsNullOrWhiteSpace(path)
            && !Workspace.IsCredentialPath(path)
            && workspace.TryGetFullPath(path, out full, out _)
            && !Workspace.IsCredentialPath(full);
    }

    private static bool TryReadText(string fullPath, out string text)
    {
        text = string.Empty;
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists
                || info.Length > WorkspaceLimits.MaximumWriteBytes
                || Workspace.LooksBinary(fullPath))
            {
                return false;
            }

            text = StrictUtf8.GetString(File.ReadAllBytes(fullPath));
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DecoderFallbackException)
        {
            return false;
        }
    }
}
