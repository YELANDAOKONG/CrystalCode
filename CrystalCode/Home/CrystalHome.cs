namespace CrystalCode.Home;

/// <summary>
/// Resolves the <c>~/.crystal</c> data directory and its well-known files.
/// </summary>
public sealed class CrystalHome
{
    public const string EnvironmentVariableName = "CRYSTAL_HOME";
    private const string DirectoryName = ".crystal";

    public CrystalHome(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ConfigPath => Path.Combine(Root, "config.json");

    public string CredentialsPath => Path.Combine(Root, "credentials.json");

    public string PermissionsPath => Path.Combine(Root, "permissions.json");

    public string SessionsDirectory => Path.Combine(Root, "sessions");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public string PluginsDirectory => Path.Combine(Root, "plugins");

    public string ToolsDirectory => Path.Combine(Root, "tools");

    public string PromptsDirectory => Path.Combine(Root, "prompts");

    public string InstructionsPath => Path.Combine(Root, "instructions.md");

    public static CrystalHome Resolve(string? root = null)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            return new CrystalHome(root);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new CrystalHome(fromEnvironment);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException(
                "The user profile directory is not available.");
        }

        return new CrystalHome(Path.Combine(userProfile, DirectoryName));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PluginsDirectory);
        Directory.CreateDirectory(ToolsDirectory);
    }
}
