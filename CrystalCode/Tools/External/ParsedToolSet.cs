namespace CrystalCode.Tools.External;

/// <summary>
/// A parsed tool set directory. Instantiation happens in <see cref="ExternalCatalog"/>.
/// </summary>
public sealed record ParsedToolSet
{
    public ParsedToolSet(
        string directory,
        string directoryName,
        ExternalRunnerKind runner,
        IReadOnlyList<string> command,
        bool stdin,
        bool enabled,
        int timeoutSeconds,
        ExternalCatalogSelection catalogs,
        IReadOnlyList<ExternalToolSpec> tools,
        string? assembly = null,
        IReadOnlyList<string>? types = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(tools);

        Directory = Path.GetFullPath(directory);
        DirectoryName = directoryName;
        Runner = runner;
        Command = command;
        Stdin = stdin;
        Enabled = enabled;
        TimeoutSeconds = timeoutSeconds;
        Catalogs = catalogs;
        Tools = tools;
        Assembly = assembly;
        Types = types ?? [];
    }

    public string Directory { get; }

    public string DirectoryName { get; }

    public ExternalRunnerKind Runner { get; }

    public IReadOnlyList<string> Command { get; }

    public bool Stdin { get; }

    public bool Enabled { get; }

    public int TimeoutSeconds { get; }

    public ExternalCatalogSelection Catalogs { get; }

    public IReadOnlyList<ExternalToolSpec> Tools { get; }

    public string? Assembly { get; }

    public IReadOnlyList<string> Types { get; }

    public override string ToString() => DirectoryName;
}
