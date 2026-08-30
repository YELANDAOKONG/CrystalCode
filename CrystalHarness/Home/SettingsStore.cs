using System.Text.Json;

using CrystalHarness.Approvals;
using CrystalHarness.Configuration;

namespace CrystalHarness.Home;

/// <summary>
/// Reads and writes <c>config.json</c> under a Crystal home directory.
/// </summary>
public sealed class SettingsStore
{
    private readonly CrystalHome _home;

    public SettingsStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public HarnessSettings LoadOrCreate()
    {
        _home.EnsureCreated();
        if (!File.Exists(_home.ConfigPath))
        {
            var created = HarnessSettings.CreateDefault();
            Save(created);
            return created;
        }

        return Load();
    }

    public HarnessSettings Load()
    {
        var json = File.ReadAllText(_home.ConfigPath);
        var document = JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? new SettingsDocument();
        var catalog = ProviderCatalog.CreateStarter()
            .Overlay(SettingsMapper.ReadProviders(document.Providers));
        var defaults = HarnessSettings.CreateDefault();
        var provider = string.IsNullOrWhiteSpace(document.Provider)
            ? defaults.Provider
            : ProviderName.Parse(document.Provider);
        var model = string.IsNullOrWhiteSpace(document.Model)
            ? defaults.Model
            : document.Model.Trim();
        var approval = string.IsNullOrWhiteSpace(document.Approval)
            ? defaults.Approval
            : ApprovalMode.Parse(document.Approval);
        var thinkingEffort = string.IsNullOrWhiteSpace(document.ThinkingEffort)
            ? defaults.ThinkingEffort
            : ThinkingSelection.Parse(document.ThinkingEffort);

        return new HarnessSettings(
            provider,
            model,
            approval,
            document.CompactionThreshold ?? defaults.CompactionThreshold,
            catalog,
            thinkingEffort);
    }

    public void Save(HarnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _home.EnsureCreated();

        var document = new SettingsDocument
        {
            Provider = settings.Provider.Value,
            Model = settings.Model,
            Approval = settings.Approval.Value,
            ThinkingEffort = settings.ThinkingEffort == ThinkingSelection.Default
                ? null
                : settings.ThinkingEffort.Value,
            CompactionThreshold = settings.CompactionThreshold,
            Providers = SettingsMapper.WriteProviders(settings.Catalog)
        };
        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(_home.ConfigPath, json);
    }
}
