namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>One plugin directory selected by discovery, before the assembly loads.</summary>
internal sealed record DiscoveredPlugin(string DirectoryName, PluginSource Source, PluginManifest Manifest);
