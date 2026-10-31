using CrystalCode.Plugins.Cli;
using CrystalCode.Plugins.Cli.Interfaces;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>One process command and the plugin directory that supplied it.</summary>
public sealed record PluginCliRegistration(string DirectoryName, IPluginCliCommand Command);
