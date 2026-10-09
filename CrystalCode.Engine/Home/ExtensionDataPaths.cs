namespace CrystalCode.Engine.Home;

/// <summary>
/// Resolved global and project runtime-data directories for one extension.
/// The global directory lives under the data directory and is shared across
/// workspaces. The project directory lives under the workspace's
/// <c>.crystal</c> tree. The host creates both when the extension first
/// receives them, at plugin attach or before a tool call.
/// </summary>
public sealed record ExtensionDataPaths
{
    private const string CrystalDirectoryName = ".crystal";
    private const string DataDirectoryName = "data";
    private const string ToolsKindName = "tools";
    private const string PluginsKindName = "plugins";
    private const int MaximumNameLength = 64;

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
    /// <param name="home">The resolved Crystal home directory.</param>
    /// <param name="workspaceRoot">The current workspace root.</param>
    /// <param name="kind">The extension kind that owns the directory.</param>
    /// <param name="directoryName">
    /// The extension directory name, its identity. It must follow the extension
    /// directory-name rule: 1-64 characters, a letter first, then letters,
    /// digits, '.', '_', or '-'. That also keeps both resolved paths inside
    /// their trees.
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
        EnsureDirectoryName(directoryName);
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

    /// <summary>
    /// Enforces the extension directory-name rule that discovery applies to
    /// plugins and tool sets. Letters, digits, '.', '_', and '-' cannot act
    /// as path separators, so both resolved paths stay inside their trees.
    /// </summary>
    private static void EnsureDirectoryName(string directoryName)
    {
        var conforms = directoryName.Length <= MaximumNameLength
            && char.IsAsciiLetter(directoryName[0]);
        if (conforms)
        {
            foreach (var character in directoryName)
            {
                if (!char.IsAsciiLetterOrDigit(character)
                    && character is not ('.' or '_' or '-'))
                {
                    conforms = false;
                    break;
                }
            }
        }

        if (!conforms)
        {
            throw new ArgumentException(
                "Extension directory name must be 1-64 characters, start with a letter, then letters, digits, '.', '_', or '-'.",
                nameof(directoryName));
        }
    }
}
