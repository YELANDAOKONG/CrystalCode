using CrystalCode.Home;
using CrystalCode.Prompts;
using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Resolves the configured export root under Home or the workspace.
/// </summary>
public static class ExportDirectory
{
    public const string DefaultRelativePath = "exports";

    public const string WorkspaceKeyword = "workspace";

    public const string ProjectKeyword = "project";

    public static string Resolve(string? configured, CrystalHome home, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(home.Root, DefaultRelativePath);
        }

        var trimmed = configured.Trim();
        if (IsWorkspaceKeyword(trimmed))
        {
            return Path.Combine(
                Path.GetFullPath(workspaceRoot),
                PromptStore.ProjectDirectoryName,
                DefaultRelativePath);
        }

        return ResolvePath(trimmed, home.Root);
    }

    public static bool TryResolve(
        string? configured,
        CrystalHome home,
        string workspaceRoot,
        out string path,
        out string error)
    {
        path = string.Empty;
        error = string.Empty;
        try
        {
            path = Resolve(configured, home, workspaceRoot);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
        {
            error = "Export directory is not valid  " + exception.Message;
            return false;
        }
    }

    public static string ResolvePath(string path, string homeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(homeRoot);
        var expanded = Workspace.Expand(path.Trim());
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(Path.Combine(homeRoot, expanded));
    }

    private static bool IsWorkspaceKeyword(string value) =>
        string.Equals(value, WorkspaceKeyword, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, ProjectKeyword, StringComparison.OrdinalIgnoreCase);
}
