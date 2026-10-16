using CrystalCode.Plugins.Status;

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
