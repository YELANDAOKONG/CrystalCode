using CrystalCode.Plugins.Status;
using CrystalCode.Plugins.Status.Interfaces;

namespace CrystalCode.Engine.Plugins;

/// <summary>One status field admitted from a disk plugin.</summary>
public sealed record PluginStatusFieldRegistration(
    string Name,
    string DirectoryName,
    string Description,
    IPluginStatusField Field)
{
    public override string ToString() => Name;
}
