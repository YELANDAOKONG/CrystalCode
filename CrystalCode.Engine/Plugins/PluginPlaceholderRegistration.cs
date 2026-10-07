using CrystalCode.Plugins.Placeholders;

namespace CrystalCode.Engine.Plugins;

/// <summary>One placeholder admitted from a disk plugin.</summary>
public sealed record PluginPlaceholderRegistration(
    string Name,
    string DirectoryName,
    IPluginPlaceholder Placeholder)
{
    public override string ToString() => Name;
}
