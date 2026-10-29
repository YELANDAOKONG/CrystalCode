namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One operator plugin. <see cref="Enabled"/> is the manifest switch, null
/// when it is not set. <see cref="Effective"/> is false when a Project
/// plugin of the same directory name replaces a Home one. <see cref="Loaded"/>
/// is true when it is active in this session. <see cref="Error"/> is empty
/// unless it failed to load or activate.
/// </summary>
public sealed record PluginCatalogEntry(
    string Directory,
    string Source,
    string? Name,
    bool? Enabled,
    bool Effective,
    bool Loaded,
    int Tools,
    int Commands,
    int Hooks,
    string Error);
