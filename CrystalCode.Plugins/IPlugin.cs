namespace CrystalCode.Plugins;

/// <summary>
/// Entry type for one disk plugin. The host loads it in an isolated context
/// and calls the public parameterless constructor.
/// </summary>
public interface IPlugin
{
    /// <summary>Gets the plugin name shown to the operator.</summary>
    string Name { get; }

    /// <summary>Returns the tools, factories, commands, and hooks from this plugin.</summary>
    PluginContribution Contribute();
}
