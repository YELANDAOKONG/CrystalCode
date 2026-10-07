namespace CrystalCode.Plugins.Models;

/// <summary>
/// Live session and review model. Each read returns the current values.
/// </summary>
public interface IPluginModels
{
    PluginModel Session { get; }

    PluginReview Review { get; }
}
