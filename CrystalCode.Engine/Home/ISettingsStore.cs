using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Loads and persists the host settings without prescribing the storage medium.
/// </summary>
public interface ISettingsStore
{
    HarnessSettings LoadOrCreate();

    HarnessSettings Load();

    void Save(HarnessSettings settings);
}
