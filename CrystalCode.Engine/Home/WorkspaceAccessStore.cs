using System.Text.Json;

using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Default extra directories for new sessions, keyed by canonical workspace
/// root. The file lives under Home so a project cannot grant itself access.
/// </summary>
public sealed class WorkspaceAccessStore
{
    public const string ReadError = "access.json could not be read.";
    public const string SaveError = "access.json could not be saved.";
    public const string ConflictError = "Default directory access for this workspace conflicts.";

    private readonly CrystalHome _home;
    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public WorkspaceAccessStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public bool TryList(
        string workspaceRoot,
        out IReadOnlyList<DirectoryAccess> rules,
        out string error)
    {
        rules = [];
        if (!TryRead(workspaceRoot, out var document, out var key, out error))
        {
            return false;
        }

        rules = Listed(Matching(document, key));
        return true;
    }

    /// <summary>
    /// Rules safe to copy into a new session. A corrupt file or conflicting
    /// modes for one directory yields no rules. Missing or credential
    /// directories are skipped and left on disk.
    /// </summary>
    public bool TrySeed(
        string workspaceRoot,
        out IReadOnlyList<DirectoryAccess> rules,
        out string error,
        out IReadOnlyList<string> skipped)
    {
        rules = [];
        skipped = [];
        if (!TryRead(workspaceRoot, out var document, out var key, out error))
        {
            return false;
        }

        var stored = Listed(Matching(document, key));
        if (Conflicts(stored))
        {
            error = ConflictError;
            return false;
        }

        var accepted = new Dictionary<string, DirectoryAccess>(PathComparer());
        var notes = new List<string>();
        foreach (var rule in stored)
        {
            if (!TryAccept(rule, out var seeded, out var skip))
            {
                notes.Add(skip);
                continue;
            }

            accepted[seeded.Path] = seeded;
        }

        rules = accepted.Values.ToArray();
        skipped = notes;
        return true;
    }

    public bool TrySet(string workspaceRoot, DirectoryAccess rule, out string error)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!TryPrepare(rule, out var stored, out error))
        {
            return false;
        }

        if (!TryRead(workspaceRoot, out var document, out var key, out error))
        {
            return false;
        }

        var matches = Matching(document, key);
        if (matches.Count > 1)
        {
            error = ConflictError;
            return false;
        }

        var entry = matches.Count == 0
            ? new WorkspaceAccessEntry { Path = key }
            : matches[0];
        entry.Path = key;
        entry.Directories ??= [];
        entry.Directories.RemoveAll(existing => existing is not null && Same(existing.Path, stored.Path));
        entry.Directories.Add(stored);
        if (matches.Count == 0)
        {
            document.Workspaces.Add(entry);
        }

        return TryWrite(document, out error);
    }

    public bool TryRemove(
        string workspaceRoot,
        string entryPath,
        string? canonicalPath,
        out bool removed,
        out string error)
    {
        removed = false;
        if (!TryRead(workspaceRoot, out var document, out var key, out error))
        {
            return false;
        }

        var matches = Matching(document, key);
        if (matches.Count > 1)
        {
            error = ConflictError;
            return false;
        }

        if (matches.Count == 0)
        {
            return true;
        }

        var entry = matches[0];
        entry.Directories ??= [];
        var before = entry.Directories.Count;
        entry.Directories.RemoveAll(rule => rule is not null && MatchesRemoval(rule, entryPath, canonicalPath));
        removed = entry.Directories.Count != before;
        if (!removed)
        {
            return true;
        }

        if (entry.Directories.Count == 0)
        {
            document.Workspaces.Remove(entry);
        }

        return TryWrite(document, out error);
    }

    private bool TryPrepare(DirectoryAccess rule, out DirectoryAccess stored, out string error)
    {
        stored = rule;
        error = string.Empty;
        if (rule.Mode is not ("read" or "write"))
        {
            error = "Access mode must be read or write.";
            return false;
        }

        if (!IsAbsolutePath(rule.Path)
            || (rule.SourcePath is not null && !IsAbsolutePath(rule.SourcePath)))
        {
            error = "Directory is not a valid path.";
            return false;
        }

        if (Workspace.IsCredentialPath(rule.Path)
            || (rule.SourcePath is not null && Workspace.IsCredentialPath(rule.SourcePath)))
        {
            error = "Credential directories cannot be granted access.";
            return false;
        }

        var path = Trim(rule.Path);
        var canonical = CanonicalKey(path);
        if (canonical is null)
        {
            error = "Directory is not a valid path.";
            return false;
        }

        path = canonical;
        if (!Directory.Exists(path))
        {
            error = "Directory not found.";
            return false;
        }

        var source = rule.SourcePath is null ? null : Trim(rule.SourcePath);
        if (source is not null && Same(source, path))
        {
            source = null;
        }

        stored = new DirectoryAccess(path, rule.Mode, source);
        return true;
    }

    private static bool TryAccept(DirectoryAccess rule, out DirectoryAccess seeded, out string skip)
    {
        seeded = rule;
        skip = "Default directory access is invalid.";
        if (rule.Mode is not ("read" or "write")
            || !IsAbsolutePath(rule.Path)
            || (rule.SourcePath is not null && !IsAbsolutePath(rule.SourcePath)))
        {
            return false;
        }

        if (Workspace.IsCredentialPath(rule.Path)
            || (rule.SourcePath is not null && Workspace.IsCredentialPath(rule.SourcePath)))
        {
            skip = "Credential directories cannot be granted access.";
            return false;
        }

        var listed = Trim(rule.Path);
        var path = CanonicalKey(listed);
        if (path is null)
        {
            return false;
        }

        if (!Directory.Exists(path))
        {
            skip = "Default directory not found: " + listed + ".";
            return false;
        }

        var source = rule.SourcePath is null ? null : Trim(rule.SourcePath);
        if (source is not null && Same(source, path))
        {
            source = null;
        }

        seeded = new DirectoryAccess(path, rule.Mode, source);
        skip = string.Empty;
        return true;
    }

    private static bool Conflicts(IReadOnlyList<DirectoryAccess> rules)
    {
        var modes = new Dictionary<string, string>(PathComparer());
        foreach (var rule in rules)
        {
            if (rule.Mode is not ("read" or "write") || !IsAbsolutePath(rule.Path))
            {
                continue;
            }

            var path = Trim(rule.Path);
            var canonical = CanonicalKey(path);
            if (canonical is null)
            {
                continue;
            }

            path = canonical;
            if (modes.TryGetValue(path, out var existing) && existing != rule.Mode)
            {
                return true;
            }

            modes[path] = rule.Mode;
        }

        return false;
    }

    private bool TryRead(
        string workspaceRoot,
        out WorkspaceAccessDocument document,
        out string key,
        out string error)
    {
        document = new WorkspaceAccessDocument();
        key = string.Empty;
        error = string.Empty;
        if (!TryWorkspaceKey(workspaceRoot, out key, out error))
        {
            return false;
        }

        if (!File.Exists(_home.AccessPath))
        {
            return true;
        }

        try
        {
            document = JsonSerializer.Deserialize<WorkspaceAccessDocument>(
                    File.ReadAllText(_home.AccessPath),
                    HomeJson.Options)
                ?? new WorkspaceAccessDocument();
            document.Workspaces ??= [];
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ReadError;
            return false;
        }
    }

    private bool TryWrite(WorkspaceAccessDocument document, out string error)
    {
        error = string.Empty;
        try
        {
            if (document.Workspaces.Count == 0)
            {
                if (File.Exists(_home.AccessPath))
                {
                    File.Delete(_home.AccessPath);
                }

                return true;
            }

            _home.EnsureCreated();
            AtomicFile.WriteText(
                _home.AccessPath,
                JsonSerializer.Serialize(document, HomeJson.Options),
                ownerOnly: true,
                followLink: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = SaveError;
            return false;
        }
    }

    private static List<WorkspaceAccessEntry> Matching(WorkspaceAccessDocument document, string key)
    {
        var matches = new List<WorkspaceAccessEntry>();
        foreach (var entry in document.Workspaces)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            if (Same(entry.Path, key) || CanonicalKey(entry.Path) is { } canonical && Same(canonical, key))
            {
                matches.Add(entry);
            }
        }

        return matches;
    }

    private static IReadOnlyList<DirectoryAccess> Listed(IReadOnlyList<WorkspaceAccessEntry> entries)
    {
        var rules = new List<DirectoryAccess>();
        foreach (var entry in entries)
        {
            if (entry.Directories is null)
            {
                continue;
            }

            foreach (var rule in entry.Directories)
            {
                if (rule is not null)
                {
                    rules.Add(rule);
                }
            }
        }

        return rules;
    }

    private static bool MatchesRemoval(DirectoryAccess rule, string entryPath, string? canonicalPath) =>
        Same(rule.Path, entryPath)
        || (rule.SourcePath is not null && Same(rule.SourcePath, entryPath))
        || (canonicalPath is not null && Same(rule.Path, canonicalPath))
        || (canonicalPath is not null && rule.SourcePath is not null && Same(rule.SourcePath, canonicalPath));

    private static bool TryWorkspaceKey(string workspaceRoot, out string key, out string error)
    {
        key = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            error = "Directory cannot be empty.";
            return false;
        }

        var canonical = CanonicalKey(workspaceRoot);
        if (canonical is null)
        {
            error = "Directory is not a valid path.";
            return false;
        }

        key = canonical;
        return true;
    }

    private static string? CanonicalKey(string path)
    {
        try
        {
            return Trim(Workspace.Canonicalize(Path.GetFullPath(path)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            _ = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static string Trim(string path) => Path.TrimEndingDirectorySeparator(path);

    private static bool Same(string left, string right) =>
        string.Equals(Trim(left), Trim(right), Comparison)
        || CanonicalKey(left) is { } leftKey
            && CanonicalKey(right) is { } rightKey
            && string.Equals(leftKey, rightKey, Comparison);

    private static IEqualityComparer<string> PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
