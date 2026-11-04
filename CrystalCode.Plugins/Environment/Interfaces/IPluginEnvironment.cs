namespace CrystalCode.Plugins.Environment.Interfaces;

/// <summary>
/// Read-only catalogs captured after plugins, external tools, and skills
/// have been loaded. A plugin cannot change these lists.
/// </summary>
public interface IPluginEnvironment
{
    /// <summary>Gets the resolved absolute Home root for this process, or empty before attachment.</summary>
    string HomeDirectory { get; }

    IReadOnlyList<PluginPeer> Plugins { get; }

    IReadOnlyList<ExternalToolSetPeer> ToolSets { get; }

    IReadOnlyList<ExternalToolPeer> ExternalTools { get; }

    IReadOnlyList<SkillPeer> Skills { get; }
}
