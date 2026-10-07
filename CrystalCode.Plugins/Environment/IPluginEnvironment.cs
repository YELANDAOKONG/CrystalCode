namespace CrystalCode.Plugins.Environment;

/// <summary>
/// Read-only catalogs captured after plugins, external tools, and skills
/// have been loaded. A plugin cannot change these lists.
/// </summary>
public interface IPluginEnvironment
{
    IReadOnlyList<PluginPeer> Plugins { get; }

    IReadOnlyList<ExternalToolSetPeer> ToolSets { get; }

    IReadOnlyList<ExternalToolPeer> ExternalTools { get; }

    IReadOnlyList<SkillPeer> Skills { get; }
}
