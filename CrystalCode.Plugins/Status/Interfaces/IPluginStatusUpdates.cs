namespace CrystalCode.Plugins.Status.Interfaces;
/// <summary>
/// Push target for one plugin's admitted status fields. The host ignores a
/// name this plugin did not register. Empty text clears the field. Calls may
/// arrive on any thread the plugin uses. A sink from a previous catalog
/// ignores later calls.
/// </summary>
public interface IPluginStatusUpdates
{
    /// <summary>Replaces the current plain text for one admitted field.</summary>
    void Set(string name, string text);

    /// <summary>Removes the current text for one admitted field.</summary>
    void Clear(string name);
}
