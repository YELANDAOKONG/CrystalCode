namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>One plugin directory that loaded.</summary>
public sealed record PluginInfo(
    string DirectoryName,
    PluginSource Source,
    string Name,
    int Tools,
    int Commands,
    int Hooks,
    int Clients,
    int Classifiers);
