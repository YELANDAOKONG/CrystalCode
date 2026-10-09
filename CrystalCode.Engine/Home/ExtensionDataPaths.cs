namespace CrystalCode.Engine.Home;

/// <summary>
/// Resolved global and project runtime-data directories for one extension.
/// The global directory lives under the data directory and is shared across
/// workspaces. The project directory lives under the workspace's
/// <c>.crystal</c> tree. The host creates both lazily on first use.
/// </summary>
public sealed record ExtensionDataPaths
{
    private const string CrystalDirectoryName = ".crystal";
    private const string DataDirectoryName = "data";
    private const string ToolsKindName = "tools";
    private const string PluginsKindName = "plugins";

    private static readonly char[] SegmentSeparators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\', ':'];

    private ExtensionDataPaths(string globalDirectory, string projectDirectory)
    {
        GlobalDirectory = globalDirectory;
        ProjectDirectory = projectDirectory;
    }

    /// <summary>Gets the global extension data directory under the data directory.</summary>
    public string GlobalDirectory { get; }

    /// <summary>Gets the per-workspace extension data directory under <c>.crystal</c>.</summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// Resolves the global and project data directories for one extension.
    /// Paths are computed only; nothing is created.
    /// </summary>
    /// <param name="home">The resolved data directory.</param>
    /// <param name="workspaceRoot">The current workspace root.</param>
    /// <param name="kind">The extension kind that owns the directory.</param>
    /// <param name="directoryName">
    /// The extension directory name, its identity. It must be one relative path
    /// segment so neither resolved path can leave its tree.
    /// </param>
    public static ExtensionDataPaths Resolve(
        CrystalHome home,
        string workspaceRoot,
        ExtensionDataKind kind,
        string directoryName)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        EnsureSingleSegment(directoryName);
        var kindName = kind switch
        {
            ExtensionDataKind.Tools => ToolsKindName,
            ExtensionDataKind.Plugins => PluginsKindName,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var global = Path.Combine(home.DataDirectory, kindName, directoryName);
        var project = Path.Combine(
            workspaceRoot,
            CrystalDirectoryName,
            DataDirectoryName,
            kindName,
            directoryName);
        return new ExtensionDataPaths(global, project);
    }

    /// <summary>
    /// Creates both directories when they are missing. The host calls this
    /// lazily when an extension first receives its directories. A directory
    /// that cannot be created is not reported; the resolved paths are still
    /// returned and the extension should create it when missing.
    /// </summary>
    public bool EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(GlobalDirectory);
            Directory.CreateDirectory(ProjectDirectory);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            return false;
        }
    }

    private static void EnsureSingleSegment(string directoryName)
    {
        if (directoryName is "." or ".."
            || directoryName.IndexOfAny(SegmentSeparators) >= 0
            || Path.IsPathRooted(directoryName))
        {
            throw new ArgumentException(
                "Extension directory name must be a single relative path segment.",
                nameof(directoryName));
        }
    }
}
