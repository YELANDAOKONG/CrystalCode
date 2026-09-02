using CrystalCode.Home;
using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Resolves explicit export output paths from slash commands.
/// </summary>
public static class ExportPath
{
    public static bool TryResolveOutputPath(
        string path,
        string workspaceRoot,
        CrystalHome home,
        out string fullPath,
        out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(home);
        error = string.Empty;
        fullPath = string.Empty;

        var expanded = Workspace.Expand(path.Trim());
        string candidate;
        if (Path.IsPathRooted(expanded))
        {
            candidate = expanded;
        }
        else
        {
            candidate = Path.Combine(Path.GetFullPath(workspaceRoot), expanded);
        }

        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "Export path is not valid.";
            return false;
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            error = "Export path is not allowed.";
            return false;
        }

        return true;
    }
}
