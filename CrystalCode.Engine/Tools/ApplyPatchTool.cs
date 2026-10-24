using System.Text;

using Crystal.Tools;

using CrystalCode.Engine.Tools.Patches;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Applies a multi-file patch of Add File, Update File, and Delete File
/// sections. The whole patch is checked before anything is written. Paths
/// outside the workspace run only after approval.
/// </summary>
public sealed class ApplyPatchTool : ITool
{
    public const string ToolName = "apply_patch";

    /// <summary>
    /// The one property of the tool's JSON input. A provider that can offer the
    /// tool as free-form text maps the model's text to this property.
    /// </summary>
    public const string TextProperty = "patchText";

    private const int MaximumPatchBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private const string ToolDescription =
        "Changes several files in one call with a patch. "
        + "patchText starts with *** Begin Patch and ends with *** End Patch. Between the markers, each file has one section. "
        + "*** Add File: <path> creates a file; every following line starts with + and is one line of the new file. "
        + "*** Update File: <path> changes an existing file. It holds one or more change blocks. "
        + "A block may begin with @@ <line>, such as a function signature, to say where to look; give several @@ lines to narrow the place step by step. "
        + "Within a block, lines starting with a space are unchanged context, lines starting with - are removed, and lines starting with + are added. "
        + "Include enough unchanged lines around an edit to find it exactly once, and keep blocks in file order. "
        + "*** End of File on its own line after a block ties that block to the end of the file. "
        + "*** Move to: <path> directly under an Update File line renames the file and applies the blocks to the result; the new path must not exist. "
        + "*** Delete File: <path> removes an existing file. "
        + "Paths are workspace-relative or absolute. "
        + "Paths inside the workspace follow the approval mode. "
        + "Outside Full mode, unlisted outside paths require approval: the Review model in Review or Audit, otherwise the operator. "
        + "Session write directories follow the approval mode; session read-only directories cannot be changed outside Full mode. "
        + "Credential paths are refused. "
        + "Every section is checked before any file changes, so a bad block leaves all files as they were. "
        + "Read each file first. A file may appear in at most one section. "
        + "Prefer edit for a change to one file. Example:\n"
        + "*** Begin Patch\n"
        + "*** Add File: notes/todo.txt\n"
        + "+first line\n"
        + "+second line\n"
        + "*** Update File: src/app.py\n"
        + "@@ def greet():\n"
        + "-    print(\"Hi\")\n"
        + "+    print(\"Hello\")\n"
        + "*** Delete File: old/unused.txt\n"
        + "*** End Patch";

    private readonly Workspace _workspace;

    public ApplyPatchTool(Workspace workspace)
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
                    "patchText": {
                      "type": "string",
                      "description": "The full patch, from *** Begin Patch to *** End Patch."
                    }
                  },
                  "required": ["patchText"]
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (!TryReadPatch(call.Arguments, out var patchText))
        {
            return Failure("Arguments must include a patchText string.");
        }

        if (Encoding.UTF8.GetByteCount(patchText) > MaximumPatchBytes)
        {
            return Failure($"The patch exceeds {MaximumPatchBytes} bytes.");
        }

        if (!PatchParser.TryParse(patchText, out var operations, out var parseError))
        {
            return Failure("The patch could not be read. " + parseError);
        }

        var prepared = new List<PreparedPatchFile>(operations.Count);
        var claimed = new HashSet<string>(PathComparer);
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (file, problem) = await PrepareAsync(operation, claimed, cancellationToken);
            if (file is null)
            {
                return Failure(problem!);
            }

            prepared.Add(file);
        }

        return await CommitAsync(prepared, cancellationToken);
    }

    /// <summary>
    /// Reads the patch text from tool arguments. Used by approval to look at
    /// the same text the tool will run.
    /// </summary>
    internal static bool TryReadPatch(string arguments, out string patchText) =>
        ToolArguments.TryReadRequiredString(arguments, TextProperty, out patchText);

    /// <summary>
    /// Every path a patch can write, delete, or create, as written in the
    /// patch. Returns false when the arguments or the patch cannot be read.
    /// </summary>
    internal static bool TryReadPaths(string arguments, out IReadOnlyList<string> paths)
    {
        paths = [];
        if (!TryReadPatch(arguments, out var patchText)
            || !PatchParser.TryParse(patchText, out var operations, out _))
        {
            return false;
        }

        paths = [.. operations.SelectMany(static operation => operation.Paths)];
        return true;
    }

    /// <summary>
    /// Every path a patch can write, delete, or create, for a front end that
    /// names the files in a call. Returns an empty list when the patch cannot
    /// be read.
    /// </summary>
    public static IReadOnlyList<string> PathsOf(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return TryReadPaths(arguments, out var paths) ? paths : [];
    }

    /// <summary>
    /// What each section of a patch removes and adds, for an approval card.
    /// Returns an empty list when the patch cannot be read.
    /// </summary>
    public static IReadOnlyList<PatchFilePreview> Preview(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!TryReadPatch(arguments, out var patchText)
            || !PatchParser.TryParse(patchText, out var operations, out _))
        {
            return [];
        }

        return [.. operations.Select(ToPreview)];
    }

    private static PatchFilePreview ToPreview(PatchOperation operation) =>
        operation switch
        {
            PatchAddition addition => new PatchFilePreview("add", addition.Path, null, [], addition.Lines),
            PatchDeletion deletion => new PatchFilePreview("delete", deletion.Path, null, [], []),
            PatchUpdate update => new PatchFilePreview(
                "update",
                update.Path,
                update.MovePath,
                [.. update.Chunks.SelectMany(static chunk => chunk.RemovedLines)],
                [.. update.Chunks.SelectMany(static chunk => chunk.AddedLines)]),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static ToolOutput Failure(string message) =>
        new(message, ToolResultStatus.Failure);

    private async ValueTask<(PreparedPatchFile? File, string? Problem)> PrepareAsync(
        PatchOperation operation,
        HashSet<string> claimed,
        CancellationToken cancellationToken)
    {
        if (!TryResolve(operation.Path, mustExist: operation is not PatchAddition, out var source, out var shown, out var problem))
        {
            return (null, problem);
        }

        if (!claimed.Add(source))
        {
            return (null, $"{shown} appears in more than one section of this patch.");
        }

        switch (operation)
        {
            case PatchAddition addition:
                return PrepareAddition(addition, source, shown);
            case PatchDeletion:
                return (new PreparedPatchFile(PatchAction.Delete, source, shown, null, null, null, null, []), null);
            case PatchUpdate update:
                return await PrepareUpdateAsync(update, source, shown, claimed, cancellationToken);
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private (PreparedPatchFile? File, string? Problem) PrepareAddition(
        PatchAddition addition,
        string source,
        string shown)
    {
        if (File.Exists(source) || Directory.Exists(source))
        {
            return (null, $"{shown} already exists; use an Update File section to change it.");
        }

        var text = addition.Lines.Count == 0
            ? string.Empty
            : string.Join('\n', addition.Lines) + "\n";
        if (Encoding.UTF8.GetByteCount(text) > WorkspaceLimits.MaximumWriteBytes)
        {
            return (null, $"The contents for {shown} exceed {WorkspaceLimits.MaximumWriteBytes} bytes.");
        }

        var regions = new[] { new SnippetRegion(1, addition.Lines.Count) };
        return (new PreparedPatchFile(PatchAction.Add, source, shown, null, null, null, text, regions), null);
    }

    private async ValueTask<(PreparedPatchFile? File, string? Problem)> PrepareUpdateAsync(
        PatchUpdate update,
        string source,
        string shown,
        HashSet<string> claimed,
        CancellationToken cancellationToken)
    {
        string? target = null;
        string? targetShown = null;
        if (update.MovePath is not null)
        {
            if (!TryResolve(update.MovePath, mustExist: false, out target, out targetShown, out var moveProblem))
            {
                return (null, moveProblem);
            }

            if (File.Exists(target) || Directory.Exists(target))
            {
                return (null, $"Cannot move {shown} to {targetShown}: the destination already exists.");
            }

            if (!claimed.Add(target))
            {
                return (null, $"{targetShown} appears in more than one section of this patch.");
            }
        }

        byte[] bytes;
        try
        {
            if (Workspace.LooksBinary(source))
            {
                return (null, $"{shown} looks binary and will not be changed.");
            }

            bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"{shown} could not be read.");
        }

        string original;
        try
        {
            original = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (null, $"{shown} is not valid UTF-8 and will not be changed.");
        }

        if (!ChunkApplier.TryApply(original, update.Chunks, out var updated, out var regions, out var applyError))
        {
            return (null, $"{shown}: {applyError}");
        }

        if (updated.Length > 0 && Encoding.UTF8.GetByteCount(updated) > WorkspaceLimits.MaximumWriteBytes)
        {
            return (null, $"The result for {shown} exceeds {WorkspaceLimits.MaximumWriteBytes} bytes.");
        }

        return (
            new PreparedPatchFile(PatchAction.Update, source, shown, target, targetShown, bytes, updated, regions),
            null);
    }

    private bool TryResolve(
        string path,
        bool mustExist,
        out string fullPath,
        out string shown,
        out string problem)
    {
        fullPath = string.Empty;
        shown = path.Trim();
        problem = string.Empty;
        if (Workspace.IsCredentialPath(path))
        {
            problem = "Changing credential paths is not allowed.";
            return false;
        }

        var resolved = mustExist
            ? _workspace.TryResolveEditableFile(path, out fullPath, out var error)
            : _workspace.TryResolveOutputPath(path, out fullPath, out error);
        if (!resolved)
        {
            problem = $"{shown}: {error}";
            return false;
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            problem = "Changing credential paths is not allowed.";
            return false;
        }

        shown = _workspace.Contains(fullPath) ? _workspace.ToRelative(fullPath) : fullPath;
        return true;
    }

    private async ValueTask<ToolOutput> CommitAsync(
        IReadOnlyList<PreparedPatchFile> files,
        CancellationToken cancellationToken)
    {
        var done = new List<PreparedPatchFile>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var problem = await WriteAsync(file, cancellationToken);
            if (problem is not null)
            {
                return Failure(PartialFailure(file, problem, done));
            }

            done.Add(file);
        }

        return new ToolOutput(Summarize(done));
    }

    private static string PartialFailure(
        PreparedPatchFile failed,
        string problem,
        IReadOnlyList<PreparedPatchFile> done)
    {
        var text = $"Stopped at {failed.SourceShown}: {problem}";
        if (done.Count == 0)
        {
            return text + " No file was changed.";
        }

        return text + " Already applied: " + string.Join(", ", done.Select(static file => file.ShownPath)) + ".";
    }

    private static async ValueTask<string?> WriteAsync(
        PreparedPatchFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (file.Action)
            {
                case PatchAction.Add:
                    return await WriteNewAsync(file, cancellationToken);
                case PatchAction.Update:
                    return await WriteUpdateAsync(file, cancellationToken);
                case PatchAction.Delete:
                    File.Delete(file.SourcePath);
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(file));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "the file could not be written.";
        }
    }

    private static async ValueTask<string?> WriteNewAsync(
        PreparedPatchFile file,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(file.SourcePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };
        try
        {
            await using var stream = new FileStream(file.SourcePath, options);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(file.NewText!), cancellationToken);
        }
        catch (IOException) when (File.Exists(file.SourcePath))
        {
            return "the file appeared while the patch was being applied.";
        }

        return null;
    }

    private static async ValueTask<string?> WriteUpdateAsync(
        PreparedPatchFile file,
        CancellationToken cancellationToken)
    {
        // The bytes read during checking must still be on disk, otherwise the
        // blocks were matched against text that is gone.
        var current = await File.ReadAllBytesAsync(file.SourcePath, cancellationToken);
        if (!current.AsSpan().SequenceEqual(file.OriginalBytes))
        {
            return "the file changed while the patch was being applied.";
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(file.NewText!);
        if (!file.IsMove)
        {
            await File.WriteAllBytesAsync(file.SourcePath, bytes, cancellationToken);
            return null;
        }

        // Write the destination first, so a failure leaves the original in place.
        var directory = Path.GetDirectoryName(file.TargetPath!);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };
        try
        {
            await using var stream = new FileStream(file.TargetPath!, options);
            await stream.WriteAsync(bytes, cancellationToken);
        }
        catch (IOException) when (File.Exists(file.TargetPath!))
        {
            return "the destination appeared while the patch was being applied.";
        }

        File.Delete(file.SourcePath);
        return null;
    }

    private static string Summarize(IReadOnlyList<PreparedPatchFile> files)
    {
        var builder = new StringBuilder("Applied patch:");
        foreach (var file in files)
        {
            builder.Append(ToolOutputText.LineSeparator);
            builder.Append(Describe(file));
        }

        foreach (var file in files)
        {
            if (file.NewText is null || file.Action == PatchAction.Delete)
            {
                continue;
            }

            var snippet = SnippetText.Around(file.NewText, file.Regions);
            if (snippet.Length == 0)
            {
                continue;
            }

            builder.Append(ToolOutputText.LineSeparator);
            builder.Append(ToolOutputText.LineSeparator);
            builder.Append(file.ShownPath);
            builder.Append(':');
            builder.Append(ToolOutputText.LineSeparator);
            builder.Append(snippet);
        }

        return ToolOutputText.Truncate(builder.ToString());
    }

    private static string Describe(PreparedPatchFile file) =>
        file.Action switch
        {
            PatchAction.Add => "A " + file.SourceShown,
            PatchAction.Delete => "D " + file.SourceShown,
            _ when file.IsMove => "R " + file.SourceShown + " -> " + file.TargetShown,
            _ => "M " + file.SourceShown
        };
}
