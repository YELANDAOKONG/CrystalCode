using CrystalCode.Plugins.Cli;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>One process command and the plugin directory that supplied it.</summary>
public sealed record PluginCliRegistration(string DirectoryName, IPluginCliCommand Command);
