using CrystalCode.Plugins.Environment;

namespace CrystalCode.Plugins;

/// <summary>
/// Entry type for one disk plugin. The host loads it in an isolated context
/// and calls the public parameterless constructor.
/// </summary>
public interface IPlugin
{
    /// <summary>Gets the plugin name shown to the operator.</summary>
    string Name { get; }

    /// <summary>Returns the tools, factories, commands, hooks, and placeholders from this plugin.</summary>
    PluginContribution Contribute();

    /// <summary>
    /// Receives the catalog snapshot after plugins, external tools, and skills
    /// have been loaded. The host calls this again when those catalogs reload.
    /// <see cref="Contribute"/> has already returned, so the snapshot is not
    /// available there. The default does nothing.
    /// </summary>
    void Attach(IPluginEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
    }
}
