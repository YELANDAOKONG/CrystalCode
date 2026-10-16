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

            rules[path] = new DirectoryAccess(path, rule.Mode);
        }

        access = Array.AsReadOnly(rules.Values.ToArray());
        return true;
    }
}
