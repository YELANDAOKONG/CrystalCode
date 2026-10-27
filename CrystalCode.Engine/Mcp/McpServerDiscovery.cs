using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Finds Crystal-owned MCP configuration. Project replaces Home on the same
/// server name.
/// </summary>
internal sealed class McpServerDiscovery
{
    private readonly CrystalHome _home;

    public McpServerDiscovery(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    /// <summary>
    /// Returns the servers to connect, in name order: valid and enabled. A
    /// Project entry replaces the Home entry of the same name even when the
    /// Project entry is invalid or disabled. Skipped servers are reported
    /// through <paramref name="notes"/>.
    /// </summary>
    public IReadOnlyList<McpServerDefinition> Collect(string workspaceRoot, IList<string> notes) =>
        [.. Effective(workspaceRoot, notes)
            .Where(entry => entry.Definition is { Enabled: true })
            .Select(entry => entry.Definition!)];

    /// <summary>
    /// Adds front-end session servers after Home and Project. A session
    /// server replaces a configured one of the same name. The result stays in
    /// name order.
    /// </summary>
    public static IReadOnlyList<McpServerDefinition> WithSession(
        IReadOnlyList<McpServerDefinition> configured,
        IReadOnlyList<McpServerDefinition> session)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(session);
        if (session.Count == 0)
        {
            return configured;
        }

        var merged = configured.ToDictionary(static server => server.Name, StringComparer.Ordinal);
        foreach (var server in session)
        {
            if (server.Source != McpServerSource.Session)
            {
                throw new ArgumentException("A front-end MCP server must use the Session source.", nameof(session));
            }

            merged[server.Name] = server;
        }

        return [.. merged.Values.OrderBy(static server => server.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every entry that takes effect, including invalid and disabled ones, in
    /// name order. Inventory uses it so a disabled server stays listed.
    /// </summary>
    internal IReadOnlyList<McpConfigEntry> Effective(string workspaceRoot, IList<string> notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(notes);

        var merged = new Dictionary<string, McpConfigEntry>(StringComparer.Ordinal);
        foreach (var scope in new[]
                 {
                     Home(notes),
                     Project(workspaceRoot, notes)
                 })
        {
            foreach (var entry in scope)
            {
                merged[entry.Name] = entry;
            }
        }

        return [.. merged.Values.OrderBy(entry => entry.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The entries of one source alone, so a name defined in both Home and
    /// Project can be shown or changed in either place.
    /// </summary>
    internal IReadOnlyList<McpConfigEntry> Scope(
        McpServerSource source,
        string workspaceRoot,
        IList<string> notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(notes);
        return [.. (source == McpServerSource.Home ? Home(notes) : Project(workspaceRoot, notes))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)];
    }

    internal static string ProjectConfigPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, McpFiles.CrystalDirectory, McpFiles.FileName);

    private IEnumerable<McpConfigEntry> Home(IList<string> notes) =>
        Scan(_home.McpConfigPath, _home.McpDirectory, McpServerSource.Home, notes);

    private static IEnumerable<McpConfigEntry> Project(string workspaceRoot, IList<string> notes)
    {
        var project = Path.Combine(workspaceRoot, McpFiles.CrystalDirectory);
        return Scan(
            Path.Combine(project, McpFiles.FileName),
            Path.Combine(project, McpFiles.DirectoryName),
            McpServerSource.Project,
            notes);
    }

    /// <summary>
    /// Reads one scope. A name defined by both <c>mcp.json</c> and a
    /// <c>server.json</c> directory is ambiguous, so neither is used.
    /// </summary>
    private static IEnumerable<McpConfigEntry> Scan(
        string configPath,
        string directoryPath,
        McpServerSource source,
        IList<string> notes)
    {
        var scope = new Dictionary<string, McpConfigEntry>(StringComparer.Ordinal);
        foreach (var entry in ReadConfig(configPath, source, notes)
                     .Concat(ReadDirectory(directoryPath, source, notes)))
        {
            if (scope.TryGetValue(entry.Name, out var first))
            {
                notes.Add(
                    $"MCP server '{entry.Name}' was skipped: the name appears in both "
                    + $"'{first.Origin}' and '{entry.Origin}'.");
                scope[entry.Name] = new McpConfigEntry(
                    entry.Name,
                    null,
                    first.Origin,
                    $"the name appears in both '{first.Origin}' and '{entry.Origin}'.");
                continue;
            }

            scope[entry.Name] = entry;
        }

        return scope.Values;
    }

    private static IReadOnlyList<McpConfigEntry> ReadConfig(
        string path,
        McpServerSource source,
        IList<string> notes)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        if (!TryReadText(path, out var json, out var problem))
        {
            notes.Add($"MCP file '{path}' was skipped: {problem}");
            return [];
        }

        return McpConfigParser.ParseFile(json, source, path, notes);
    }

    private static List<McpConfigEntry> ReadDirectory(
        string root,
        McpServerSource source,
        IList<string> notes)
    {
        string[] directories;
        try
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                return [];
            }

            directories = Directory.GetDirectories(fullRoot);
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or NotSupportedException
                                              or IOException
                                              or UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(directories, StringComparer.Ordinal);
        var entries = new List<McpConfigEntry>();
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!McpServerNames.IsServerName(name))
            {
                notes.Add($"MCP directory '{name}' was skipped: the name is invalid.");
                continue;
            }

            var manifest = Path.Combine(directory, McpFiles.ServerFileName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            if (!TryReadText(manifest, out var json, out var problem))
            {
                notes.Add($"MCP server '{name}' ({manifest}) was skipped: {problem}");
                entries.Add(new McpConfigEntry(name, null, manifest, problem, FromDirectory: true));
                continue;
            }

            entries.Add(McpConfigParser.ParseSingle(json, name, source, manifest, notes) with
            {
                FromDirectory = true
            });
        }

        return entries;
    }

    private static bool TryReadText(string path, out string json, out string problem)
    {
        json = string.Empty;
        problem = string.Empty;
        try
        {
            if (new FileInfo(path).Length > McpFiles.MaximumFileBytes)
            {
                problem = "the file is too large.";
                return false;
            }

            json = File.ReadAllText(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = "the file could not be read.";
            return false;
        }
    }
}
