using CrystalCode.Engine.Plugins.Disk;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// One plugin directory read from its manifest. The assembly is not loaded.
/// </summary>
public sealed record PluginEntry
{
    public PluginEntry(
        string directoryName,
        PluginSource source,
        string manifestPath,
        bool? enabled,
        bool effective,
        string assemblyPath,
        string typeName,
        string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(assemblyPath);
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(error);
        DirectoryName = directoryName;
        Source = source;
        ManifestPath = manifestPath;
        Enabled = enabled;
        Effective = effective;
        AssemblyPath = assemblyPath;
        TypeName = typeName;
        Error = error;
    }

    public string DirectoryName { get; }

    public PluginSource Source { get; }

    public string ManifestPath { get; }

    public bool? Enabled { get; }

    public bool Effective { get; }

    public string AssemblyPath { get; }

    public string TypeName { get; }

    public string Error { get; }

    public override string ToString() => DirectoryName;
}
