using CrystalCode.Home;

namespace CrystalCode.Tools.External;

/// <summary>
/// Finds Crystal-owned <c>tools.json</c> directories. Project overlay wins
/// on the same directory name.
/// </summary>
public sealed class ToolSetDiscovery
{
    private readonly CrystalHome _home;

    public ToolSetDiscovery(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public IReadOnlyList<ParsedToolSet> Collect(string workspaceRoot, IList<string> notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(notes);

        var sets = new Dictionary<string, ParsedToolSet>(ExternalToolNames.OverlayComparer);
        Scan(_home.ToolsDirectory, sets, notes);
        Scan(
            Path.Combine(workspaceRoot, ExternalFiles.CrystalDirectory, ExternalFiles.DirectoryName),
            sets,
            notes);
        return [.. sets.Values
            .Where(item => item.Enabled)
            .OrderBy(item => item.DirectoryName, ExternalToolNames.OverlayComparer)];
    }

    private static void Scan(
        string root,
        Dictionary<string, ParsedToolSet> sets,
        IList<string> notes)
    {
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return;
        }

        if (!Directory.Exists(fullRoot))
        {
            return;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(fullRoot);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        Array.Sort(directories, StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!ExternalToolNames.IsDirectoryName(name))
            {
                notes.Add($"External tool set '{name}' was skipped: directory name is invalid.");
                continue;
            }

            var manifest = Path.Combine(directory, ExternalFiles.FileName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(manifest);
            }
            catch (IOException)
            {
                notes.Add($"External tool set '{name}' was skipped: tools.json could not be read.");
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                notes.Add($"External tool set '{name}' was skipped: tools.json could not be read.");
                continue;
            }

            if (!ToolsManifestParser.TryParse(directory, json, out var set, out var error)
                || set is null)
            {
                notes.Add($"External tool set '{name}' was skipped: {error}");
                continue;
            }

            sets[name] = set;
        }
    }
}
