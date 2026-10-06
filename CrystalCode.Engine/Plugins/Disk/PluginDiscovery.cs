using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Finds Crystal-owned plugin.json directories. A project directory replaces
/// the home directory of the same name.
/// </summary>
internal sealed class PluginDiscovery
{
    private readonly CrystalHome _home;

    public PluginDiscovery(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public IReadOnlyList<DiscoveredPlugin> Collect(string workspaceRoot, IList<string> notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(notes);
        var found = new Dictionary<string, DiscoveredPlugin>(ExternalToolNames.OverlayComparer);
        Scan(_home.PluginsDirectory, PluginSource.Home, found, notes);
        Scan(
            Path.Combine(workspaceRoot, PluginFiles.CrystalDirectory, PluginFiles.DirectoryName),
            PluginSource.Project,
            found,
            notes);
        return [.. found.Values
            .Where(static item => item.Manifest.Enabled)
            .OrderBy(static item => item.DirectoryName, ExternalToolNames.OverlayComparer)];
    }

    private static void Scan(
        string root,
        PluginSource source,
        Dictionary<string, DiscoveredPlugin> found,
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
                notes.Add($"Plugin '{name}' was skipped: directory name is invalid.");
                continue;
            }

            var manifestPath = Path.Combine(directory, PluginFiles.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            if (source == PluginSource.Project)
            {
                found.Remove(name);
            }

            string json;
            try
            {
                json = File.ReadAllText(manifestPath);
            }
            catch (IOException)
            {
                notes.Add($"Plugin '{name}' was skipped: plugin.json could not be read.");
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                notes.Add($"Plugin '{name}' was skipped: plugin.json could not be read.");
                continue;
            }

            if (!PluginManifestParser.TryParse(directory, json, out var manifest, out var error)
                || manifest is null)
            {
                notes.Add($"Plugin '{name}' was skipped: {error}");
                continue;
            }

            found[name] = new DiscoveredPlugin(name, source, manifest);
        }
    }
}
