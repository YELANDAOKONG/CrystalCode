namespace CrystalCode.Plugins.Data;

/// <summary>
/// Opt-in for receiving this plugin's runtime-data directories. Implementing
/// this interface is what tells the host to hand over the directories. The
/// host creates them before the call. This is the plugin counterpart of the
/// <c>ToolHostContext</c> data directories an external tool receives.
/// </summary>
public interface IPluginDataDirectory
{
    /// <summary>
    /// Receives this plugin's global and project runtime-data directories. The
    /// global directory is shared across workspaces and lives under the data
    /// directory. The project directory belongs to the current workspace.
    /// </summary>
    /// <param name="globalDataDirectory">The global data directory for this plugin.</param>
    /// <param name="projectDataDirectory">The project data directory for this plugin.</param>
    void AttachDataDirectories(string globalDataDirectory, string projectDataDirectory);
}
