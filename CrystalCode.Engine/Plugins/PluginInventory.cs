using CrystalCode.Engine.Catalog;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Lists plugin manifests and turns one directory on or off.
/// A project directory of the same name is the one that takes effect.
/// </summary>
public static class PluginInventory
{
    public const string DiscoveryOff =
        "Plugin discovery is off. Enabled plugins will not load until plugin discovery is turned on.";

    public static IReadOnlyList<PluginEntry> List(CrystalHome home, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var homeEntries = Scan(home.PluginsDirectory, PluginSource.Home);
        var projectRoot = Path.Combine(workspaceRoot, PluginFiles.CrystalDirectory, PluginFiles.DirectoryName);
        var projectEntries = Scan(projectRoot, PluginSource.Project);
        return Compose(homeEntries, projectEntries);
    }

    public static bool TryFind(
        CrystalHome home,
        string workspaceRoot,
        string directoryName,
        PluginSource? source,
        out PluginEntry? entry,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        entry = null;
        error = string.Empty;
        if (!TryChoose(home, workspaceRoot, directoryName, source, out var chosen, out var name, out error))
        {
            return false;
        }

        entry = List(home, workspaceRoot).First(item =>
            item.Source == chosen
            && ExternalToolNames.OverlayComparer.Equals(item.DirectoryName, name));
        return true;
    }

    public static bool TrySetEnabled(
        CrystalHome home,
        string workspaceRoot,
        string directoryName,
        PluginSource? source,
        bool enabled,
        out PluginEntry? entry,
        out bool changed,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        entry = null;
        changed = false;
        error = string.Empty;
        if (!TryChoose(home, workspaceRoot, directoryName, source, out var chosen, out var name, out error))
        {
            return false;
        }

        var path = ManifestPath(home, workspaceRoot, name, chosen);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "plugin.json could not be read.";
            return false;
        }

        if (!PluginManifestParser.TryParse(Path.GetDirectoryName(path)!, json, out _, out var parseError))
        {
            error = parseError;
            return false;
        }

        if (!JsonEnabledField.TryRead(json, out var current, out var document, out var readError) || document is null)
        {
            error = readError;
            return false;
        }

        if (current != enabled)
        {
            try
            {
                JsonEnabledField.Write(path, document, enabled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = "plugin.json could not be written.";
                return false;
            }

            changed = true;
        }

        return TryFind(home, workspaceRoot, name, chosen, out entry, out error);
    }

    public static IReadOnlyList<string> Format(IReadOnlyList<PluginEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return ["No plugins."];
        }

        IReadOnlyList<string> headers = ["Directory", "Source", "Enabled", "Effective", "Path", "Assembly", "Type"];
        var rows = entries.Select(Row).ToArray();
        return AlignedRows.Format(headers, rows);
    }

    public static IReadOnlyList<string> Format(PluginEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string Label, string Value)>
        {
            ("Directory", entry.DirectoryName),
            ("Source", SourceName(entry.Source)),
            ("Enabled", Flag(entry.Enabled)),
            ("Effective", Flag(entry.Effective)),
            ("Path", entry.ManifestPath),
            ("Assembly", Dash(entry.AssemblyPath)),
            ("Type", Dash(entry.TypeName))
        };
        if (entry.Error.Length > 0)
        {
            fields.Add(("Error", entry.Error));
        }

        return AlignedRows.Fields(fields);
    }

    private static bool TryChoose(
        CrystalHome home,
        string workspaceRoot,
        string directoryName,
        PluginSource? source,
        out PluginSource chosen,
        out string name,
        out string error)
    {
        chosen = PluginSource.Home;
        name = directoryName.Trim();
        error = string.Empty;
        if (name.Length == 0 || !ExternalToolNames.IsDirectoryName(name))
        {
            error = "Directory name is invalid.";
            return false;
        }

        var homePath = ManifestPath(home, workspaceRoot, name, PluginSource.Home);
        var projectPath = ManifestPath(home, workspaceRoot, name, PluginSource.Project);
        var homeExists = File.Exists(homePath);
        var projectExists = File.Exists(projectPath);
        if (source is PluginSource requested)
        {
            var exists = requested == PluginSource.Home ? homeExists : projectExists;
            if (!exists)
            {
                error = $"Plugin '{name}' was not found in {SourceName(requested)}.";
                return false;
            }

            chosen = requested;
            return true;
        }

        if (projectExists)
        {
            chosen = PluginSource.Project;
            return true;
        }

        if (homeExists)
        {
            chosen = PluginSource.Home;
            return true;
        }

        error = $"Plugin '{name}' was not found.";
        return false;
    }

    private static string ManifestPath(
        CrystalHome home,
        string workspaceRoot,
        string directoryName,
        PluginSource source)
    {
        var root = source == PluginSource.Home
            ? home.PluginsDirectory
            : Path.Combine(workspaceRoot, PluginFiles.CrystalDirectory, PluginFiles.DirectoryName);
        return Path.Combine(root, directoryName, PluginFiles.FileName);
    }

    private static List<Located> Scan(string root, PluginSource source)
    {
        var found = new List<Located>();
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return found;
        }

        if (!Directory.Exists(fullRoot))
        {
            return found;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(fullRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        Array.Sort(directories, StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var manifest = Path.Combine(directory, PluginFiles.FileName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            found.Add(new Located(Path.GetFileName(directory), source, directory, manifest));
        }

        return found;
    }

    private static IReadOnlyList<PluginEntry> Compose(List<Located> home, List<Located> project)
    {
        var projectNames = project
            .Where(item => ExternalToolNames.IsDirectoryName(item.Name))
            .Select(item => item.Name)
            .ToHashSet(ExternalToolNames.OverlayComparer);
        var entries = new List<PluginEntry>();
        foreach (var item in home.Concat(project))
        {
            var valid = ExternalToolNames.IsDirectoryName(item.Name);
            var effective = valid
                && (item.Source == PluginSource.Project || !projectNames.Contains(item.Name));
            entries.Add(Read(item, effective, valid));
        }

        return entries
            .OrderBy(item => item.DirectoryName, ExternalToolNames.OverlayComparer)
            .ThenBy(item => item.Source)
            .ToArray();
    }

    private static PluginEntry Read(Located item, bool effective, bool validName)
    {
        if (!validName)
        {
            return new PluginEntry(
                item.Name,
                item.Source,
                item.Manifest,
                null,
                false,
                string.Empty,
                string.Empty,
                "directory name is invalid.");
        }

        string json;
        try
        {
            json = File.ReadAllText(item.Manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PluginEntry(
                item.Name,
                item.Source,
                item.Manifest,
                null,
                effective,
                string.Empty,
                string.Empty,
                "plugin.json could not be read.");
        }

        if (!PluginManifestParser.TryParse(item.Directory, json, out var manifest, out var error)
            || manifest is null)
        {
            return new PluginEntry(
                item.Name,
                item.Source,
                item.Manifest,
                null,
                effective,
                string.Empty,
                string.Empty,
                error);
        }

        return new PluginEntry(
            item.Name,
            item.Source,
            item.Manifest,
            manifest.Enabled,
            effective,
            manifest.AssemblyPath,
            manifest.TypeName,
            string.Empty);
    }

    private static IReadOnlyList<string> Row(PluginEntry entry) =>
    [
        entry.DirectoryName,
        SourceName(entry.Source),
        Flag(entry.Enabled),
        Flag(entry.Effective),
        entry.ManifestPath,
        Dash(entry.AssemblyPath),
        string.IsNullOrEmpty(entry.Error) ? Dash(entry.TypeName) : entry.Error
    ];

    private static string SourceName(PluginSource source) =>
        source == PluginSource.Home ? "Home" : "Project";

    private static string Flag(bool? value) =>
        value switch
        {
            true => "Yes",
            false => "No",
            _ => "-"
        };

    private static string Dash(string value) => value.Length == 0 ? "-" : value;

    private sealed record Located(string Name, PluginSource Source, string Directory, string Manifest);
}
