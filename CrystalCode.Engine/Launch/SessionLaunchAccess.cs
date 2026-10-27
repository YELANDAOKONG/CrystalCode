using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Launch;

/// <summary>
/// Reads <c>--allow-read</c> and <c>--allow-write</c> into canonical session
/// directory rules. The rules are session metadata, never preferences.
/// </summary>
public static class SessionLaunchAccess
{
    public static bool TryRead(
        SessionLaunchOptions settings,
        string workspaceRoot,
        out IReadOnlyList<DirectoryAccess> access,
        out string error)
    {
        var workspace = new Workspace(workspaceRoot);
        // Keep the operator's lexical path. Rebasing onto the canonical root
        // would resolve an ancestor link such as /var -> /private/var.
        var lexicalRoot = Path.GetFullPath(workspaceRoot);
        var rules = new Dictionary<string, DirectoryAccess>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        access = [];
        error = string.Empty;
        var requested = settings.AllowRead.Select(path => new DirectoryAccess(path, "read"))
            .Concat(settings.AllowWrite.Select(path => new DirectoryAccess(path, "write")));
        foreach (var rule in requested)
        {
            if (!workspace.TryResolve(rule.Path, out var fullPath, out error))
            {
                return false;
            }

            if (Workspace.IsCredentialPath(rule.Path) || Workspace.IsCredentialPath(fullPath))
            {
                error = "Credential directories cannot be granted access.";
                return false;
            }

            var path = Path.TrimEndingDirectorySeparator(fullPath);
            if (rules.TryGetValue(path, out var existing) && existing.Mode != rule.Mode)
            {
                error = "A directory cannot have both --allow-read and --allow-write.";
                return false;
            }

            var sourcePath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Workspace.Expand(rule.Path.Trim()), lexicalRoot));
            rules[path] = new DirectoryAccess(path, rule.Mode,
                string.Equals(path, sourcePath, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal) ? null : sourcePath);
        }

        access = Array.AsReadOnly(rules.Values.ToArray());
        return true;
    }

    /// <summary>
    /// Reads additional roots: each must be absolute, an existing directory
    /// outside the workspace, and not a credential directory. Each becomes a
    /// write rule, in request order, without repeats.
    /// </summary>
    public static bool TryReadRoots(
        IReadOnlyList<string> roots,
        string workspaceRoot,
        out IReadOnlyList<DirectoryAccess> access,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var workspace = new Workspace(workspaceRoot);
        var rules = new List<DirectoryAccess>(roots.Count);
        access = [];
        error = string.Empty;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                error = "Each additional directory must be an absolute path.";
                return false;
            }

            if (!workspace.TryResolve(root, out var fullPath, out error))
            {
                error = "Additional directory not found  " + root;
                return false;
            }

            if (Workspace.IsCredentialPath(root) || Workspace.IsCredentialPath(fullPath))
            {
                error = "Credential directories cannot be granted access.";
                return false;
            }

            var path = Path.TrimEndingDirectorySeparator(fullPath);
            if (workspace.Contains(path))
            {
                error = "An additional directory must be outside the workspace  " + root;
                return false;
            }

            if (rules.Any(rule => string.Equals(rule.Path, path, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)))
            {
                continue;
            }

            var sourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            rules.Add(new DirectoryAccess(path, "write",
                string.Equals(path, sourcePath, StringComparison.Ordinal) ? null : sourcePath));
        }

        access = Array.AsReadOnly(rules.ToArray());
        return true;
    }
}
