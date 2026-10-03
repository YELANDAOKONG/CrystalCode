namespace CrystalCode.Engine.Tools;

/// <summary>
/// Resolves tool paths from a workspace root. Reads and writes may leave the
/// root after approval. Credential paths stay forbidden. Symbolic links resolve
/// to their final target.
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
        Root = Canonicalize(Path.GetFullPath(root));
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
            candidate = Canonicalize(Path.GetFullPath(combined));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
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
        TryResolve(path, mustExistAsFile: true, allowOutside: false, out fullPath, out error);

    public bool TryResolveReadableFile(string path, out string fullPath, out string error) =>
        TryResolve(path, mustExistAsFile: true, allowOutside: true, out fullPath, out error);

    public bool TryResolveWritablePath(string path, out string fullPath, out string error) =>
        TryResolve(path, mustExistAsFile: false, allowOutside: false, out fullPath, out error);

    public bool TryResolveOutputPath(string path, out string fullPath, out string error) =>
        TryResolve(path, mustExistAsFile: false, allowOutside: true, out fullPath, out error);

    public bool TryResolveEditableFile(string path, out string fullPath, out string error) =>
        TryResolve(path, mustExistAsFile: true, allowOutside: true, out fullPath, out error);

    public bool TryResolveExistingLocation(
        string path,
        out string fullPath,
        out string error) =>
        TryResolveLocation(path, allowOutside: false, out fullPath, out error);

    public bool TryResolveReadableLocation(
        string path,
        out string fullPath,
        out string error) =>
        TryResolveLocation(path, allowOutside: true, out fullPath, out error);

    public bool TryGetFullPath(string path, out string fullPath, out string error) =>
        TryNormalize(path, allowOutside: true, out fullPath, out error);

    public bool Contains(string fullPath) => IsInsideRoot(fullPath);

    public static bool IsCredentialPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var expanded = Expand(path).Replace('\\', '/').TrimEnd('/');
        var segments = expanded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index].Equals(".ssh", StringComparison.OrdinalIgnoreCase)
                || segments[index].Equals(".gnupg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (index + 1 < segments.Length
                && segments[index].Equals(".crystal", StringComparison.OrdinalIgnoreCase)
                && segments[index + 1].Equals("credentials.json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<string> EnumerateFiles(string directoryFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryFullPath);

        var start = Canonicalize(Path.GetFullPath(directoryFullPath));
        var fence = IsInsideRoot(start) ? Root : start;
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(start);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current) || !IsInside(current, fence))
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
                if (!TryAcceptEnumerated(file, fence, out var resolved))
                {
                    continue;
                }

                yield return resolved;
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

                if (!TryAcceptEnumerated(child, fence, out var resolved))
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

    private bool TryResolveLocation(string path, bool allowOutside, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (!TryNormalize(path, allowOutside, out var candidate, out error))
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

    private bool TryResolve(
        string path,
        bool mustExistAsFile,
        bool allowOutside,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (!TryNormalize(path, allowOutside, out var candidate, out error))
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

    private bool TryNormalize(string path, bool allowOutside, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path cannot be empty.";
            return false;
        }

        var expanded = Expand(path.Trim());
        var combined = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(Root, expanded);
        string candidate;
        try
        {
            candidate = Canonicalize(Path.GetFullPath(combined));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
        {
            error = "Path is not valid.";
            return false;
        }

        if (!allowOutside && !IsInsideRoot(candidate))
        {
            error = "Path is outside the workspace.";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private bool IsInsideRoot(string fullPath) => IsInside(fullPath, Root);

    private static bool IsInside(string fullPath, string root)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal)
            || string.Equals(fullPath, root, StringComparison.Ordinal);
    }

    private bool TryAcceptEnumerated(string path, string fence, out string resolved)
    {
        resolved = path;
        if (!IsReparsePoint(path))
        {
            return IsInside(path, fence);
        }

        try
        {
            resolved = Canonicalize(path);
        }
        catch (Exception exception) when (IsSkippableIo(exception))
        {
            return false;
        }

        return IsInside(resolved, fence);
    }

    internal static string Canonicalize(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var rooted = Path.GetFullPath(fullPath);
        var root = Path.GetPathRoot(rooted);
        if (string.IsNullOrEmpty(root))
        {
            return rooted;
        }

        var current = root;
        var relative = rooted[root.Length..];
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                var parent = Path.GetDirectoryName(TrimTrailingSeparator(current));
                current = string.IsNullOrEmpty(parent) ? root : parent;
                continue;
            }

            current = Path.Combine(TrimTrailingSeparator(current), segment);
            if (!IsReparsePoint(current))
            {
                continue;
            }

            if (!TryFinalTarget(current, out var target))
            {
                throw new IOException($"Symbolic link could not be resolved: {current}");
            }

            current = target;
        }

        return Path.GetFullPath(current);
    }

    private static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is not null && string.Equals(path, root, StringComparison.Ordinal))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryFinalTarget(string path, out string target)
    {
        target = string.Empty;
        FileSystemInfo? link;
        try
        {
            link = File.ResolveLinkTarget(path, returnFinalTarget: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }

        if (link is null)
        {
            return false;
        }

        target = link.FullName;
        return true;
    }

    private static bool IsSkippableIo(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException;
}
