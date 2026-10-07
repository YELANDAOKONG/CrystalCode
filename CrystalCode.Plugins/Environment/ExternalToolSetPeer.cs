namespace CrystalCode.Plugins.Environment;

/// <summary>
/// One discovered external tool set. <see cref="Enabled"/> is null when the
/// manifest could not say.
/// </summary>
public sealed record ExternalToolSetPeer
{
    public ExternalToolSetPeer(
        string directory,
        string source,
        bool? enabled,
        bool effective,
        bool loaded,
        string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(error);
        if (source is not ("home" or "project"))
        {
            throw new ArgumentException("Source must be home or project.", nameof(source));
        }

        Directory = directory;
        Source = source;
        Enabled = enabled;
        Effective = effective;
        Loaded = loaded;
        Error = error;
    }

    public string Directory { get; }

    public string Source { get; }

    public bool? Enabled { get; }

    public bool Effective { get; }

    public bool Loaded { get; }

    public string Error { get; }

    public override string ToString() => Directory;
}
