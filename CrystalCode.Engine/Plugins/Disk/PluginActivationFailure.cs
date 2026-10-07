namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>A plugin directory discovery selected and then could not load.</summary>
public sealed record PluginActivationFailure(string DirectoryName, PluginSource Source, string Error)
{
    public override string ToString() => DirectoryName;
}
