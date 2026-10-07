namespace CrystalCode.Plugins.Environment;

/// <summary>
/// One discovered plugin directory. <see cref="Enabled"/> is null when the
/// manifest could not say. <see cref="Name"/> is empty until the plugin loads.
/// </summary>
public sealed record PluginPeer
{
    public PluginPeer(
        string directory,
        string source,
        string name,
        bool? enabled,
        bool effective,
        bool loaded,
        string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(error);
        if (source is not ("home" or "project"))
        {
            throw new ArgumentException("Source must be home or project.", nameof(source));
        }

        Directory = directory;
        Source = source;
        Name = name;
        Enabled = enabled;
        Effective = effective;
        Loaded = loaded;
        Error = error;
    }

    public string Directory { get; }

    public string Source { get; }

    public string Name { get; }

    public bool? Enabled { get; }

    public bool Effective { get; }

    public bool Loaded { get; }

    public string Error { get; }

    public override string ToString() => Directory;
}
