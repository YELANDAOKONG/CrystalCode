using CrystalCode.Engine.Catalog;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Lists prompt attachments and edits <c>enabled</c> or <c>order</c> in
/// <c>prompt.json</c>. A workspace directory wins over Home. These methods
/// do not write <c>config.json</c>.
/// </summary>
public static class PromptAttachmentInventory
{
    public static IReadOnlyList<PromptCatalogEntry> List(CrystalHome home, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var project = Path.Combine(workspaceRoot, PromptStore.ProjectDirectoryName, "prompt-attachments");
        var homeRows = Scan(home.PromptAttachmentsDirectory, "Home");
        var projectRows = Scan(project, "Workspace");
        var projectNames = new HashSet<string>(
            projectRows.Where(row => row.Error.Length == 0).Select(row => row.DirectoryName),
            StringComparer.Ordinal);
        var rows = new List<PromptCatalogEntry>();
        foreach (var row in homeRows)
        {
            var hidden = row.Error.Length == 0 && projectNames.Contains(row.DirectoryName);
            rows.Add(hidden ? row with { Effective = false } : row);
        }

        rows.AddRange(projectRows);
        return rows;
    }

    public static bool TrySetEnabled(
        CrystalHome home,
        string workspaceRoot,
        string name,
        string? source,
        bool enabled,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!TryDirectory(home, workspaceRoot, name, source, out var directory, out var normalized, out error))
        {
            return false;
        }

        var before = EnabledOrder(home, workspaceRoot);
        if (!PromptManifestFile.TrySetEnabled(directory, enabled, out error))
        {
            return false;
        }

        if (!enabled || !EditsWinningCopy(home, workspaceRoot, normalized, directory))
        {
            return true;
        }

        foreach (var item in before)
        {
            if (string.Equals(item, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var order = new List<string>(before) { normalized };
        return TryAssignOrders(home, workspaceRoot, order, out error);
    }

    public static bool TryMove(
        CrystalHome home,
        string workspaceRoot,
        string name,
        bool earlier,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var normalized = name.Trim();
        var order = EnabledOrder(home, workspaceRoot).ToList();
        var index = order.FindIndex(item => string.Equals(item, normalized, StringComparison.Ordinal));
        if (index < 0)
        {
            error = $"Prompt attachment '{normalized}' is not enabled.";
            return false;
        }

        var target = earlier ? index - 1 : index + 1;
        if (target < 0)
        {
            error = $"Prompt attachment '{normalized}' is already first.";
            return false;
        }

        if (target >= order.Count)
        {
            error = $"Prompt attachment '{normalized}' is already last.";
            return false;
        }

        (order[index], order[target]) = (order[target], order[index]);
        return TryAssignOrders(home, workspaceRoot, order, out error);
    }

    public static IReadOnlyList<string> Format(IReadOnlyList<PromptCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return ["No prompt attachments."];
        }

        IReadOnlyList<string> headers =
            ["Directory", "Source", "Enabled", "Effective", "Name", "Description", "Order", "Path"];
        var rows = entries.Select(Row).ToArray();
        return AlignedRows.Format(headers, rows);
    }

    public static IReadOnlyList<string> Format(PromptCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string Label, string Value)>
        {
            ("Directory", entry.DirectoryName),
            ("Source", entry.Source),
            ("Enabled", Flag(entry.Enabled)),
            ("Effective", Flag(entry.Effective)),
            ("Name", Dash(entry.Title)),
            ("Description", Dash(entry.Description)),
            ("Order", entry.Order is int order ? order.ToString() : "-"),
            ("Path", entry.ManifestPath)
        };
        if (entry.Error.Length > 0)
        {
            fields.Add(("Error", entry.Error));
        }

        return AlignedRows.Fields(fields);
    }

    private static IReadOnlyList<string> EnabledOrder(CrystalHome home, string workspaceRoot)
    {
        var notes = new List<string>();
        var project = new CrystalHome(Path.Combine(workspaceRoot, PromptStore.ProjectDirectoryName));
        var catalog = new PromptAttachmentDiscovery().Collect(home, project, notes);
        var ordered = new List<PromptAttachmentDefinition>();
        foreach (var name in catalog.Names)
        {
            if (catalog.TryGet(name, out var definition) && definition.Manifest.Enabled)
            {
                ordered.Add(definition);
            }
        }

        return ordered
            .OrderBy(item => item.Manifest.Order ?? int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name)
            .ToArray();
    }

    private static bool EditsWinningCopy(
        CrystalHome home,
        string workspaceRoot,
        string name,
        string directory)
    {
        var notes = new List<string>();
        var project = new CrystalHome(Path.Combine(workspaceRoot, PromptStore.ProjectDirectoryName));
        var catalog = new PromptAttachmentDiscovery().Collect(home, project, notes);
        if (!catalog.TryGet(name, out var definition))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(definition.Directory),
            Path.GetFullPath(directory),
            StringComparison.Ordinal);
    }

    private static bool TryAssignOrders(
        CrystalHome home,
        string workspaceRoot,
        IReadOnlyList<string> names,
        out string error)
    {
        error = string.Empty;
        var notes = new List<string>();
        var project = new CrystalHome(Path.Combine(workspaceRoot, PromptStore.ProjectDirectoryName));
        var catalog = new PromptAttachmentDiscovery().Collect(home, project, notes);
        for (var index = 0; index < names.Count; index++)
        {
            if (!catalog.TryGet(names[index], out var definition))
            {
                error = $"Prompt attachment '{names[index]}' was not found.";
                return false;
            }

            if (!PromptManifestFile.TrySetOrder(definition.Directory, index, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDirectory(
        CrystalHome home,
        string workspaceRoot,
        string name,
        string? source,
        out string directory,
        out string normalized,
        out string error)
    {
        directory = string.Empty;
        normalized = name.Trim();
        error = string.Empty;
        if (!PromptAttachmentNames.IsValid(normalized))
        {
            error = "Directory name is invalid.";
            return false;
        }

        var homeDirectory = Path.Combine(home.PromptAttachmentsDirectory, normalized);
        var projectDirectory = Path.Combine(
            workspaceRoot,
            PromptStore.ProjectDirectoryName,
            "prompt-attachments",
            normalized);
        var homeExists = File.Exists(PromptManifestFile.PathFor(homeDirectory));
        var projectExists = File.Exists(PromptManifestFile.PathFor(projectDirectory));
        if (string.Equals(source, "home", StringComparison.Ordinal))
        {
            directory = homeDirectory;
            if (!homeExists)
            {
                error = $"Prompt attachment '{normalized}' was not found in Home.";
                return false;
            }
        }
        else if (string.Equals(source, "project", StringComparison.Ordinal))
        {
            directory = projectDirectory;
            if (!projectExists)
            {
                error = $"Prompt attachment '{normalized}' was not found in Workspace.";
                return false;
            }
        }
        else if (projectExists)
        {
            directory = projectDirectory;
        }
        else if (homeExists)
        {
            directory = homeDirectory;
        }
        else
        {
            error = $"Prompt attachment '{normalized}' was not found.";
            return false;
        }

        return PromptManifestFile.TryRead(directory, out _, out error);
    }

    private static IReadOnlyList<PromptCatalogEntry> Scan(string root, string source)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(directories, StringComparer.Ordinal);
        var rows = new List<PromptCatalogEntry>(directories.Length);
        foreach (var directory in directories)
        {
            rows.Add(Read(directory, source));
        }

        return rows;
    }

    private static PromptCatalogEntry Read(string directory, string source)
    {
        var name = Path.GetFileName(directory);
        var path = PromptManifestFile.PathFor(directory);
        if (!PromptAttachmentNames.IsValid(name))
        {
            return Problem(name, source, path, "directory name is invalid.");
        }

        if (!PromptManifestDirectory.HasPrompt(directory))
        {
            return Problem(name, source, path, "no prompt files were found.");
        }

        if (!PromptManifestFile.TryRead(directory, out var manifest, out var error) || manifest is null)
        {
            return Problem(name, source, path, error);
        }

        return new PromptCatalogEntry(
            name,
            source,
            manifest.Enabled,
            manifest.Enabled,
            PromptManifestDirectory.Title(name, manifest),
            manifest.Description,
            manifest.Order,
            path,
            string.Empty);
    }

    private static PromptCatalogEntry Problem(string name, string source, string path, string error) =>
        new(name, source, false, false, name, string.Empty, null, path, error);

    private static IReadOnlyList<string> Row(PromptCatalogEntry entry) =>
    [
        entry.DirectoryName,
        entry.Source,
        Flag(entry.Enabled),
        Flag(entry.Effective),
        Dash(entry.Error.Length == 0 ? entry.Title : entry.Error),
        Dash(entry.Description),
        entry.Order is int order ? order.ToString() : "-",
        entry.ManifestPath
    ];

    private static string Flag(bool value) => value ? "yes" : "no";

    private static string Dash(string value) => value.Length == 0 ? "-" : value;
}
