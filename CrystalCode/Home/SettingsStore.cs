using System.Text.Json;
using CrystalCode.Approvals;
using CrystalCode.Configuration;

namespace CrystalCode.Home;

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
        var externalToolApproval = ReadExternalToolApproval(
            document.ExternalToolApproval,
            defaults.ExternalToolApproval);

        return new HarnessSettings(
            provider,
            model,
            approval,
            document.CompactionThreshold ?? defaults.CompactionThreshold,
            catalog,
            thinkingEffort,
            document.Skills ?? defaults.Skills,
            document.ExternalTools ?? defaults.ExternalTools,
            document.EstimatedTokens ?? defaults.EstimatedTokens,
            string.IsNullOrWhiteSpace(document.PromptSet)
                ? defaults.PromptSet
                : document.PromptSet.Trim(),
            externalToolApproval,
            string.IsNullOrWhiteSpace(document.ExportDirectory)
                ? null
                : document.ExportDirectory.Trim());
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
            Skills = settings.Skills ? null : false,
            ExternalTools = settings.ExternalTools ? null : false,
            ExternalToolApproval = WriteExternalToolApproval(settings.ExternalToolApproval),
            EstimatedTokens = settings.EstimatedTokens ? true : null,
            PromptSet = string.Equals(
                settings.PromptSet,
                HarnessSettings.DefaultPromptSet,
                StringComparison.Ordinal)
                    ? null
                    : settings.PromptSet,
            ExportDirectory = settings.ExportDirectory,
            CompactionThreshold = settings.CompactionThreshold,
            Providers = SettingsMapper.WriteProviders(settings.Catalog)
        };
        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(_home.ConfigPath, json);
    }

    private static ExternalToolApprovalSettings ReadExternalToolApproval(
        ExternalToolApprovalDocument? document,
        ExternalToolApprovalSettings defaults)
    {
        if (document is null)
        {
            return defaults;
        }

        var home = string.IsNullOrWhiteSpace(document.Home)
            ? defaults.Home
            : ExternalToolTrustPolicy.Parse(document.Home);
        var project = string.IsNullOrWhiteSpace(document.Project)
            ? defaults.Project
            : ExternalToolTrustPolicy.Parse(document.Project);
        return new ExternalToolApprovalSettings(home, project);
    }

    private static ExternalToolApprovalDocument? WriteExternalToolApproval(
        ExternalToolApprovalSettings settings)
    {
        if (settings == ExternalToolApprovalSettings.Default)
        {
            return null;
        }

        return new ExternalToolApprovalDocument
        {
            Home = settings.Home == ExternalToolApprovalSettings.Default.Home
                ? null
                : settings.Home.Value,
            Project = settings.Project == ExternalToolApprovalSettings.Default.Project
                ? null
                : settings.Project.Value
        };
    }
}
