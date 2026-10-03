using System.Text.Json;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Reads user preferences from <c>config.json</c> and provider definitions
/// from <c>providers.json</c> under a Crystal home directory.
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
            return Load();
        }

        return Load();
    }

    public HarnessSettings Load()
    {
        var json = File.ReadAllText(_home.ConfigPath);
        using var parsed = JsonDocument.Parse(json);
        var document = JsonSerializer.Deserialize<SettingsDocument>(parsed.RootElement, HomeJson.Options)
            ?? new SettingsDocument();
        var catalog = ProviderCatalog.CreateStarter()
            .Overlay(ReadProviders(document));
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
            document.VerboseTools ?? defaults.VerboseTools,
            document.VerboseCommands ?? defaults.VerboseCommands,
            string.IsNullOrWhiteSpace(document.PromptSet)
                ? defaults.PromptSet
                : document.PromptSet.Trim(),
            externalToolApproval,
            string.IsNullOrWhiteSpace(document.ExportDirectory)
                ? null
                : document.ExportDirectory.Trim(),
            new StatusLineSettings(
                document.CustomStatusLine ?? false,
                document.StatusLine),
            ExecutionBudgetMapper.Read(document.ExecutionBudget),
            BashTimeoutMapper.Read(parsed.RootElement),
            ReadApprovalModel(document.ApprovalModel));
    }

    public void Save(HarnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _home.EnsureCreated();
        var previous = File.Exists(_home.ConfigPath)
            ? JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(_home.ConfigPath), HomeJson.Options)
            : null;
        if (!File.Exists(_home.ProvidersPath)
            && previous?.Providers is { ValueKind: JsonValueKind.Object } legacyProviders)
        {
            _ = SettingsMapper.ReadProviders(legacyProviders);
            CopyProviders(legacyProviders);
        }

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
            VerboseTools = settings.VerboseTools ? null : false,
            VerboseCommands = settings.VerboseCommands ? null : false,
            PromptSet = string.Equals(
                settings.PromptSet,
                HarnessSettings.DefaultPromptSet,
                StringComparison.Ordinal)
                    ? null
                    : settings.PromptSet,
            ExportDirectory = settings.ExportDirectory,
            CustomStatusLine = settings.StatusLine.Enabled ? true : null,
            StatusLine = settings.StatusLine.Fields.SequenceEqual(
                StatusLineSettings.DefaultFields,
                StringComparer.Ordinal)
                    ? null
                    : [.. settings.StatusLine.Fields],
            CompactionThreshold = settings.CompactionThreshold,
            ExecutionBudget = ExecutionBudgetMapper.Write(settings.ExecutionBudget),
            BashTimeoutSeconds = BashTimeoutMapper.Write(settings.BashTimeoutSeconds),
            Providers = previous?.Providers,
            ApprovalModel = WriteApprovalModel(settings.ApprovalModel)
        };
        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(_home.ConfigPath, json);
    }

    private IReadOnlyList<ProviderDefinition> ReadProviders(SettingsDocument document)
    {
        if (!File.Exists(_home.ProvidersPath))
        {
            return SettingsMapper.ReadProviders(document.Providers);
        }

        using var providers = JsonDocument.Parse(File.ReadAllText(_home.ProvidersPath));
        return SettingsMapper.ReadProviders(providers.RootElement);
    }

    private void CopyProviders(JsonElement providers)
    {
        var temporaryPath = Path.Combine(_home.Root, $".providers-{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            using (var stream = new FileStream(temporaryPath, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(providers, HomeJson.Options));
            }

            File.Move(temporaryPath, _home.ProvidersPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static ApprovalModelSettings ReadApprovalModel(ApprovalModelDocument? document)
    {
        if (document is null)
        {
            return ApprovalModelSettings.Off;
        }

        var provider = string.IsNullOrWhiteSpace(document.Provider)
            ? null
            : document.Provider.Trim();
        var model = string.IsNullOrWhiteSpace(document.Model)
            ? null
            : document.Model.Trim();
        if (document.Enabled == true && (provider is null || model is null))
        {
            throw new InvalidOperationException(
                "approvalModel.enabled requires provider and model.");
        }

        return new ApprovalModelSettings(document.Enabled ?? false, provider, model);
    }

    private static ApprovalModelDocument? WriteApprovalModel(ApprovalModelSettings settings)
    {
        if (!settings.Enabled && !settings.HasSelection)
        {
            return null;
        }

        return new ApprovalModelDocument
        {
            Enabled = settings.Enabled ? true : null,
            Provider = settings.Provider,
            Model = settings.Model
        };
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
