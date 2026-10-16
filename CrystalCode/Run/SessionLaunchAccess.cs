using CrystalCode.Commands;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Run;

internal static class SessionLaunchAccess
{
    public static bool TryRead(
        SessionLaunchSettings settings,
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
}
