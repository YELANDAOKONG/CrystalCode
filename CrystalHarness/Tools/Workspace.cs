namespace CrystalHarness.Tools;

/// <summary>
/// A rooted directory that tools may read or write. Paths must stay inside Root.
/// </summary>
public sealed class Workspace
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "dist"
    };

    public Workspace(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root))
        {
            throw new DirectoryNotFoundException(
                $"Workspace directory not found: {Root}");
        }
    }

    public string Root { get; private set; }

    public bool TrySetRoot(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Directory cannot be empty.";
            return false;
        }

        var expanded = Expand(path.Trim());
        var combined = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(Root, expanded);
        string candidate;
        try
        {
            candidate = Path.GetFullPath(combined);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "Directory is not a valid path.";
            return false;
        }

        if (!Directory.Exists(candidate))
        {
            error = "Directory not found.";
            return false;
        }

        Root = candidate;
        return true;
    }

    public static string Expand(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal)
            || path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        return path;
    }

    public bool IsIgnoredDirectoryName(string name) =>
        IgnoredDirectoryNames.Contains(name);

    public string ToRelative(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var relative = Path.GetRelativePath(Root, fullPath).Replace('\\', '/');
        return relative.Length == 0 ? "." : relative;
    }

    public bool TryResolveExistingFile(string path, out string fullPath, out string error) =>
        TryResolve(path, mustExistAsFile: true, out fullPath, out error);

    public bool TryResolveWritablePath(string path, out string fullPath, out string error) =>
        TryResolve(path, mustExistAsFile: false, out fullPath, out error);

    public bool TryResolveExistingLocation(
        string path,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (!TryNormalize(path, out var candidate, out error))
        {
            return false;
        }

        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            error = $"Path not found: {ToRelative(candidate)}";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public IEnumerable<string> EnumerateFiles(string directoryFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryFullPath);

        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(Path.GetFullPath(directoryFullPath));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current) || !IsInsideRoot(current))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch (Exception exception) when (IsSkippableIo(exception))
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current);
            }
            catch (Exception exception) when (IsSkippableIo(exception))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (IsIgnoredDirectoryName(Path.GetFileName(child)))
                {
                    continue;
                }

                string resolved;
                try
                {
                    resolved = Path.GetFullPath(child);
                }
                catch (Exception exception) when (IsSkippableIo(exception))
                {
                    continue;
                }

                pending.Push(resolved);
            }
        }
    }

    public static bool LooksBinary(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        using var stream = File.OpenRead(fullPath);
        var buffer = new byte[Math.Min(WorkspaceLimits.BinaryProbeBytes, stream.Length)];
        var read = stream.Read(buffer, 0, buffer.Length);
        for (var index = 0; index < read; index++)
        {
            if (buffer[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string TruncatePreview(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= WorkspaceLimits.ConfirmPreviewCharacters)
        {
            return text;
        }

        return text[..WorkspaceLimits.ConfirmPreviewCharacters]
            + $"\n[truncated to {WorkspaceLimits.ConfirmPreviewCharacters} characters]";
    }

    private bool TryResolve(
        string path,
        bool mustExistAsFile,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (!TryNormalize(path, out var candidate, out error))
        {
            return false;
        }

        if (mustExistAsFile)
        {
            if (!File.Exists(candidate))
            {
                error = Directory.Exists(candidate)
                    ? $"Path is a directory: {ToRelative(candidate)}"
                    : $"File not found: {ToRelative(candidate)}";
                return false;
            }
        }
        else if (Directory.Exists(candidate))
        {
            error = $"Path is a directory: {ToRelative(candidate)}";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private bool TryNormalize(string path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path cannot be empty.";
            return false;
        }

        var combined = Path.IsPathRooted(path)
            ? path
            : Path.Combine(Root, path);
        string candidate;
        try
        {
            candidate = Path.GetFullPath(combined);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "Path is not valid.";
            return false;
        }

        if (!IsInsideRoot(candidate))
        {
            error = "Path is outside the workspace.";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private bool IsInsideRoot(string fullPath)
    {
        var root = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.Ordinal)
            || string.Equals(fullPath, Root, StringComparison.Ordinal);
    }

    private static bool IsSkippableIo(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException;
}
