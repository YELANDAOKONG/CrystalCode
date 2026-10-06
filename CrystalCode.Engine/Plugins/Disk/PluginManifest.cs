namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>A parsed plugin.json. The assembly path is inside the plugin directory.</summary>
internal sealed record PluginManifest(string AssemblyPath, string TypeName, bool Enabled);
