namespace CrystalCode.Engine.Tools;

/// <summary>
/// File collection and content-searchability rules shared by the workspace
/// text search tools.
/// </summary>
internal static class TextSearchFiles
{
    /// <summary>
    /// Collects candidate files for one search call. A null path searches the
    /// whole workspace. Credential paths are rejected.
    /// </summary>
    public static bool TryCollect(
        Workspace workspace,
        string? relativePath,
        out List<string> files,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        files = [];
        error = string.Empty;
        if (relativePath is null)
        {
            AddSearchable(workspace.EnumerateFiles(workspace.Root), files);
            return true;
        }

        if (Workspace.IsCredentialPath(relativePath))
        {
            error = "Searching credential paths is not allowed.";
            return false;
        }

        if (!workspace.TryResolveReadableLocation(relativePath, out var location, out error))
        {
            return false;
        }

        if (Workspace.IsCredentialPath(location))
        {
            error = "Searching credential paths is not allowed.";
            return false;
        }

        if (File.Exists(location))
        {
            files.Add(location);
            return true;
        }

        AddSearchable(workspace.EnumerateFiles(location), files);
        return true;
    }

    /// <summary>
    /// True when the file is small enough and not binary, so its contents may
    /// be searched. Name matching does not require this check.
    /// </summary>
    public static bool IsSearchable(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Length <= WorkspaceLimits.MaximumGrepFileBytes
            && !Workspace.LooksBinary(fullPath);
    }

    private static void AddSearchable(IEnumerable<string> paths, List<string> files)
    {
        foreach (var path in paths)
        {
            if (!Workspace.IsCredentialPath(path))
            {
                files.Add(path);
            }
        }
    }
}
