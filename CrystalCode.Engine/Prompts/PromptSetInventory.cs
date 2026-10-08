using CrystalCode.Engine.Catalog;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Lists home prompt sets and turns one <c>prompt.json</c> on or off.
/// Enabling one set turns the others off. These methods do not write
/// <c>config.json</c>.
/// </summary>
public static class PromptSetInventory
{
    public static IReadOnlyList<PromptCatalogEntry> List(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return Scan(home.PromptSetsDirectory);
    }

    public static bool TryEnable(CrystalHome home, string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        if (!TryDirectory(home, name, out var directory, out var normalized, out error))
        {
            return false;
        }

        if (!PromptManifestFile.TrySetEnabled(directory, true, out error))
        {
            return false;
        }

        foreach (var entry in List(home))
        {
            if (entry.Error.Length > 0
                || !entry.Enabled
                || string.Equals(entry.DirectoryName, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            if (!PromptManifestFile.TrySetEnabled(
                    Path.GetDirectoryName(entry.ManifestPath)!,
                    false,
                    out error))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryDisable(CrystalHome home, string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        if (!TryDirectory(home, name, out var directory, out _, out error))
        {
            return false;
        }

        return PromptManifestFile.TrySetEnabled(directory, false, out error);
    }

    public static bool TryDisableAll(CrystalHome home, out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        error = string.Empty;
        foreach (var entry in List(home))
        {
            if (entry.Error.Length > 0 || !entry.Enabled)
            {
                continue;
            }

            if (!PromptManifestFile.TrySetEnabled(
                    Path.GetDirectoryName(entry.ManifestPath)!,
                    false,
                    out error))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<string> Format(IReadOnlyList<PromptCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return ["No prompt sets."];
        }

        IReadOnlyList<string> headers = ["Directory", "Enabled", "Effective", "Name", "Description", "Path"];
        var rows = entries.Select(Row).ToArray();
        return AlignedRows.Format(headers, rows);
    }

    public static IReadOnlyList<string> Format(PromptCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string Label, string Value)>
        {
            ("Directory", entry.DirectoryName),
            ("Enabled", Flag(entry.Enabled)),
            ("Effective", Flag(entry.Effective)),
            ("Name", Dash(entry.Title)),
            ("Description", Dash(entry.Description)),
            ("Path", entry.ManifestPath)
        };
        if (entry.Error.Length > 0)
        {
            fields.Add(("Error", entry.Error));
        }

        return AlignedRows.Fields(fields);
    }

    private static IReadOnlyList<PromptCatalogEntry> Scan(string root)
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
        var rows = new List<PromptCatalogEntry>();
        foreach (var directory in directories)
        {
            rows.Add(Read(directory));
        }

        var enabled = 0;
        foreach (var row in rows)
        {
            if (row.Error.Length == 0 && row.Enabled)
            {
                enabled++;
            }
        }

        if (enabled == 1)
        {
            return rows;
        }

        var cleared = new List<PromptCatalogEntry>(rows.Count);
        foreach (var row in rows)
        {
            cleared.Add(row with { Effective = false });
        }

        return cleared;
    }

    private static PromptCatalogEntry Read(string directory)
    {
        var name = Path.GetFileName(directory);
        var path = PromptManifestFile.PathFor(directory);
        if (string.Equals(name, PromptSetNames.Default, StringComparison.Ordinal))
        {
            return Problem(name, path, $"'{PromptSetNames.Default}' is reserved.");
        }

        if (!PromptSetNames.IsValid(name))
        {
            return Problem(name, path, "directory name is invalid.");
        }

        if (!PromptManifestDirectory.HasNamedPrompt(directory))
        {
            return Problem(name, path, "no prompt files were found.");
        }

        if (!PromptManifestFile.TryRead(directory, out var manifest, out var error) || manifest is null)
        {
            return Problem(name, path, error);
        }

        return new PromptCatalogEntry(
            name,
            "Home",
            manifest.Enabled,
            manifest.Enabled,
            PromptManifestDirectory.Title(name, manifest),
            manifest.Description,
            manifest.Order,
            path,
            string.Empty);
    }

    private static bool TryDirectory(
        CrystalHome home,
        string name,
        out string directory,
        out string normalized,
        out string error)
    {
        directory = string.Empty;
        normalized = name.Trim();
        error = string.Empty;
        if (!PromptSetNames.IsValid(normalized))
        {
            error = "Directory name is invalid.";
            return false;
        }

        directory = Path.Combine(home.PromptSetsDirectory, normalized);
        if (!Directory.Exists(directory) || !File.Exists(PromptManifestFile.PathFor(directory)))
        {
            error = $"Prompt set '{normalized}' was not found.";
            return false;
        }

        if (!PromptManifestFile.TryRead(directory, out _, out var readError))
        {
            error = readError;
            return false;
        }

        return true;
    }

    private static PromptCatalogEntry Problem(string name, string path, string error) =>
        new(name, "Home", false, false, name, string.Empty, null, path, error);

    private static IReadOnlyList<string> Row(PromptCatalogEntry entry) =>
    [
        entry.DirectoryName,
        Flag(entry.Enabled),
        Flag(entry.Effective),
        Dash(entry.Error.Length == 0 ? entry.Title : entry.Error),
        Dash(entry.Description),
        entry.ManifestPath
    ];

    private static string Flag(bool value) => value ? "yes" : "no";

    private static string Dash(string value) => value.Length == 0 ? "-" : value;
}
