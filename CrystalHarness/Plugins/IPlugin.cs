namespace CrystalHarness.Plugins;

/// <summary>
/// In-process contribution. Disk isolation is later work.
/// </summary>
public interface IPlugin
{
    string Name { get; }

    PluginContribution Contribute();
}
