using CrystalCode.Engine.Catalog;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Lists external tool set manifests and turns one directory on or off.
/// A project directory of the same name is the one that takes effect.
/// </summary>
public static class ToolSetInventory
{
    public const string DiscoveryOff =
        "External tool discovery is off. Enabled tool sets will not load until external tool discovery is turned on.";

    public static IReadOnlyList<ToolSetEntry> List(CrystalHome home, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var homeEntries = Scan(home.ToolsDirectory, ExternalToolSource.Home);
        var projectRoot = Path.Combine(workspaceRoot, ExternalFiles.CrystalDirectory, ExternalFiles.DirectoryName);
        var projectEntries = Scan(projectRoot, ExternalToolSource.Project);
        return Compose(homeEntries, projectEntries);
    }

    public static bool TryFind(
        CrystalHome home,
        string workspaceRoot,
        string directoryName,
        ExternalToolSource? source,
        out ToolSetEntry? entry,
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
        ExternalToolSource? source,
        bool enabled,
        out ToolSetEntry? entry,
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
            error = "tools.json could not be read.";
            return false;
        }

        if (!ToolsManifestParser.TryParse(Path.GetDirectoryName(path)!, json, out _, out var parseError, chosen))
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
                error = "tools.json could not be written.";
                return false;
            }

            changed = true;
        }

        return TryFind(home, workspaceRoot, name, chosen, out entry, out error);
    }

    public static IReadOnlyList<string> Format(IReadOnlyList<ToolSetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return ["No tool sets."];
        }

        IReadOnlyList<string> headers =
            ["Directory", "Source", "Enabled", "Effective", "Path", "Runner", "Catalogs", "Approval", "Tools"];
        var rows = entries.Select(Row).ToArray();
        return AlignedRows.Format(headers, rows);
    }

    public static IReadOnlyList<string> Format(ToolSetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string Label, string Value)>
        {
            ("Directory", entry.DirectoryName),
            ("Source", SourceName(entry.Source)),
            ("Enabled", Flag(entry.Enabled)),
            ("Effective", Flag(entry.Effective)),
            ("Path", entry.ManifestPath),
            ("Runner", Dash(entry.Runner)),
            ("Catalogs", Dash(entry.Catalogs)),
            ("Approval", Dash(entry.Approval)),
            ("Tools", entry.Tools.Count == 0 ? "-" : string.Join(", ", entry.Tools))
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
        ExternalToolSource? source,
        out ExternalToolSource chosen,
        out string name,
        out string error)
    {
        chosen = ExternalToolSource.Home;
        name = directoryName.Trim();
        error = string.Empty;
        if (name.Length == 0 || !ExternalToolNames.IsDirectoryName(name))
        {
            error = "Directory name is invalid.";
            return false;
        }

        var homeExists = File.Exists(ManifestPath(home, workspaceRoot, name, ExternalToolSource.Home));
        var projectExists = File.Exists(ManifestPath(home, workspaceRoot, name, ExternalToolSource.Project));
        if (source is ExternalToolSource requested)
        {
            var exists = requested == ExternalToolSource.Home ? homeExists : projectExists;
            if (!exists)
            {
                error = $"Tool set '{name}' was not found in {SourceName(requested)}.";
                return false;
            }

            chosen = requested;
            return true;
        }

        if (projectExists)
        {
            chosen = ExternalToolSource.Project;
            return true;
        }

        if (homeExists)
        {
            chosen = ExternalToolSource.Home;
            return true;
        }

        error = $"Tool set '{name}' was not found.";
        return false;
    }

    private static string ManifestPath(
        CrystalHome home,
        string workspaceRoot,
        string directoryName,
        ExternalToolSource source)
    {
        var root = source == ExternalToolSource.Home
            ? home.ToolsDirectory
            : Path.Combine(workspaceRoot, ExternalFiles.CrystalDirectory, ExternalFiles.DirectoryName);
        return Path.Combine(root, directoryName, ExternalFiles.FileName);
    }

    private static List<Located> Scan(string root, ExternalToolSource source)
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
            var manifest = Path.Combine(directory, ExternalFiles.FileName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            found.Add(new Located(Path.GetFileName(directory), source, directory, manifest));
        }

        return found;
    }

    private static IReadOnlyList<ToolSetEntry> Compose(List<Located> home, List<Located> project)
    {
        var projectNames = project
            .Where(item => ExternalToolNames.IsDirectoryName(item.Name))
            .Select(item => item.Name)
            .ToHashSet(ExternalToolNames.OverlayComparer);
        var entries = new List<ToolSetEntry>();
        foreach (var item in home.Concat(project))
        {
            var valid = ExternalToolNames.IsDirectoryName(item.Name);
            var effective = valid
                && (item.Source == ExternalToolSource.Project || !projectNames.Contains(item.Name));
            entries.Add(Read(item, effective, valid));
        }

        return entries
            .OrderBy(item => item.DirectoryName, ExternalToolNames.OverlayComparer)
            .ThenBy(item => item.Source.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static ToolSetEntry Read(Located item, bool effective, bool validName)
    {
        if (!validName)
        {
            return Broken(item, false, "directory name is invalid.");
        }

        string json;
        try
        {
            json = File.ReadAllText(item.Manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Broken(item, effective, "tools.json could not be read.");
        }

        if (!ToolsManifestParser.TryParse(item.Directory, json, out var set, out var error, item.Source)
            || set is null)
        {
            return Broken(item, effective, error);
        }

        return new ToolSetEntry(
            item.Name,
            item.Source,
            item.Manifest,
            set.Enabled,
            effective,
            set.Runner.Value,
            Catalogs(set.Catalogs),
            set.Approval.Value,
            set.Tools.Select(Label).ToArray(),
            string.Empty);
    }

    private static ToolSetEntry Broken(Located item, bool effective, string error) =>
        new(
            item.Name,
            item.Source,
            item.Manifest,
            null,
            effective,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            error);

    private static string Label(ExternalToolSpec tool) =>
        tool.Approval == ExternalApprovalMode.Inherit
            ? tool.Name
            : tool.Name + " (" + tool.Approval.Value + ")";

    private static string Catalogs(ExternalCatalogSelection selection)
    {
        if (selection.Plan && selection.Work)
        {
            return "Plan+Work";
        }

        return selection.Plan ? "Plan" : "Work";
    }

    private static IReadOnlyList<string> Row(ToolSetEntry entry) =>
    [
        entry.DirectoryName,
        SourceName(entry.Source),
        Flag(entry.Enabled),
        Flag(entry.Effective),
        entry.ManifestPath,
        Dash(entry.Runner),
        Dash(entry.Catalogs),
        Dash(entry.Approval),
        entry.Error.Length > 0
            ? entry.Error
            : entry.Tools.Count == 0 ? "-" : string.Join(", ", entry.Tools)
    ];

    private static string SourceName(ExternalToolSource source) =>
        source == ExternalToolSource.Home ? "Home" : "Project";

    private static string Flag(bool? value) =>
        value switch
        {
            true => "Yes",
            false => "No",
            _ => "-"
        };

    private static string Dash(string value) => value.Length == 0 ? "-" : value;

    private sealed record Located(string Name, ExternalToolSource Source, string Directory, string Manifest);
}
