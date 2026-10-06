namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// One external tool set read from its manifest. The set is not started.
/// </summary>
public sealed record ToolSetEntry
{
    public ToolSetEntry(
        string directoryName,
        ExternalToolSource source,
        string manifestPath,
        bool? enabled,
        bool effective,
        string runner,
        string catalogs,
        string approval,
        IReadOnlyList<string> tools,
        string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(error);
        DirectoryName = directoryName;
        Source = source;
        ManifestPath = manifestPath;
        Enabled = enabled;
        Effective = effective;
        Runner = runner;
        Catalogs = catalogs;
        Approval = approval;
        Tools = tools;
        Error = error;
    }

    public string DirectoryName { get; }

    public ExternalToolSource Source { get; }

    public string ManifestPath { get; }

    public bool? Enabled { get; }

    public bool Effective { get; }

    public string Runner { get; }

    public string Catalogs { get; }

    public string Approval { get; }

    public IReadOnlyList<string> Tools { get; }

    public string Error { get; }

    public override string ToString() => DirectoryName;
}
