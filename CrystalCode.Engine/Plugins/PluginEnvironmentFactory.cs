using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Skills;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Plugins.Environment;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Builds the catalog snapshot a disk plugin reads. Discovery lists every
/// directory. Loaded is true only for plugins and tool sets the session
/// actually accepted, and for tools now in the model catalog.
/// </summary>
public static class PluginEnvironmentFactory
{
    public static PluginEnvironment Create(
        CrystalHome home,
        string workspaceRoot,
        PluginCatalog plugins,
        ExternalCatalog tools,
        SkillCatalog? skills)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(tools);
        return new PluginEnvironment(
            PluginPeers(home, workspaceRoot, plugins),
            ToolSetPeers(home, workspaceRoot, tools),
            ToolPeers(tools),
            SkillPeers(skills));
    }

    private static IReadOnlyList<PluginPeer> PluginPeers(
        CrystalHome home,
        string workspaceRoot,
        PluginCatalog plugins)
    {
        var peers = new List<PluginPeer>();
        foreach (var entry in PluginInventory.List(home, workspaceRoot))
        {
            var loaded = FindPlugin(plugins, entry);
            var error = entry.Error;
            if (error.Length == 0)
            {
                error = FindFailure(plugins, entry);
            }

            peers.Add(new PluginPeer(
                entry.DirectoryName,
                SourceName(entry.Source),
                loaded?.Name ?? string.Empty,
                entry.Enabled,
                entry.Effective,
                loaded is not null,
                error));
        }

        return peers;
    }

    private static IReadOnlyList<ExternalToolSetPeer> ToolSetPeers(
        CrystalHome home,
        string workspaceRoot,
        ExternalCatalog tools)
    {
        var peers = new List<ExternalToolSetPeer>();
        foreach (var entry in ToolSetInventory.List(home, workspaceRoot))
        {
            var load = FindLoad(tools, entry);
            var error = entry.Error;
            if (error.Length == 0 && load is not null && load.Error.Length > 0)
            {
                error = load.Error;
            }

            peers.Add(new ExternalToolSetPeer(
                entry.DirectoryName,
                entry.Source.Value,
                entry.Enabled,
                entry.Effective,
                load?.Loaded == true,
                error));
        }

        return peers;
    }

    private static IReadOnlyList<ExternalToolPeer> ToolPeers(ExternalCatalog tools)
    {
        var peers = new List<ExternalToolPeer>(tools.Tools.Count);
        foreach (var tool in tools.Tools)
        {
            peers.Add(new ExternalToolPeer(
                tool.Name,
                tool.SetName,
                tool.Source.Value,
                tool.Catalogs.Plan,
                tool.Catalogs.Work));
        }

        return peers;
    }

    private static IReadOnlyList<SkillPeer> SkillPeers(SkillCatalog? skills)
    {
        if (skills is null)
        {
            return [];
        }

        var peers = new List<SkillPeer>(skills.Count);
        foreach (var skill in skills.Items)
        {
            peers.Add(new SkillPeer(skill.Name, skill.Description));
        }

        return peers;
    }

    private static PluginInfo? FindPlugin(PluginCatalog plugins, PluginEntry entry)
    {
        foreach (var plugin in plugins.Plugins)
        {
            if (plugin.Source == entry.Source
                && ExternalToolNames.OverlayComparer.Equals(plugin.DirectoryName, entry.DirectoryName))
            {
                return plugin;
            }
        }

        return null;
    }

    private static string FindFailure(PluginCatalog plugins, PluginEntry entry)
    {
        foreach (var failure in plugins.Failures)
        {
            if (failure.Source == entry.Source
                && ExternalToolNames.OverlayComparer.Equals(failure.DirectoryName, entry.DirectoryName))
            {
                return failure.Error;
            }
        }

        return string.Empty;
    }

    private static ExternalSetLoad? FindLoad(ExternalCatalog tools, ToolSetEntry entry)
    {
        foreach (var load in tools.Attempted)
        {
            if (load.Source == entry.Source
                && ExternalToolNames.OverlayComparer.Equals(load.DirectoryName, entry.DirectoryName))
            {
                return load;
            }
        }

        return null;
    }

    private static string SourceName(PluginSource source) =>
        source switch
        {
            PluginSource.Home => "home",
            PluginSource.Project => "project",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
