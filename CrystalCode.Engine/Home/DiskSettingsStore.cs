using System.Text.Json;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home.Interfaces;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Reads user preferences from <c>config.json</c> and provider definitions
/// from <c>providers.json</c> and <c>providers/</c> under a Crystal home directory.
/// </summary>
public sealed class DiskSettingsStore : ISettingsStore
{
    private readonly CrystalHome _home;

    public DiskSettingsStore(CrystalHome home)
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
        var mcpToolApproval = ReadMcpToolApproval(
            document.McpToolApproval,
            defaults.McpToolApproval);

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
            document.VerboseResults ?? defaults.VerboseResults,
            document.VerboseCalls ?? defaults.VerboseCalls,
            document.VerboseApprovals ?? defaults.VerboseApprovals,
            document.VerboseReview ?? defaults.VerboseReview,
            document.VerboseThinking ?? defaults.VerboseThinking,
            defaults.PromptSet,
            externalToolApproval,
            string.IsNullOrWhiteSpace(document.ExportDirectory)
                ? null
                : document.ExportDirectory.Trim(),
            new StatusLineSettings(
                document.CustomStatusLine ?? false,
                document.StatusLine),
            ExecutionBudgetMapper.Read(document.ExecutionBudget),
            BashTimeoutMapper.Read(
                parsed.RootElement,
                BashTimeoutMapper.PropertyName,
                WorkspaceLimits.BashTimeoutSeconds),
            BashTimeoutMapper.Read(
                parsed.RootElement,
                BashTimeoutMapper.BackgroundPropertyName,
                WorkspaceLimits.BackgroundBashTimeoutSeconds),
            ReadApprovalModel(document.ApprovalModel),
            document.Plugins ?? defaults.Plugins,
            document.WorkspaceTrust ?? defaults.WorkspaceTrust,
            document.ShowCompactionSummary ?? defaults.ShowCompactionSummary,
            showAutomaticCompactionSummary: document.ShowAutomaticCompactionSummary
                ?? defaults.ShowAutomaticCompactionSummary,
            imageModel: ReadImageModel(document.ImageModel),
            contextSelections: SettingsMapper.ReadContextSelections(document.ContextSelections),
            tierSelections: SettingsMapper.ReadTokenSelections(document.TierSelections, "tierSelections"),
            speedSelections: SettingsMapper.ReadTokenSelections(document.SpeedSelections, "speedSelections"),
            retry: ReadRetry(document.Retry),
            approvalRetry: ReadRetry(document.ApprovalRetry),
            compactionModel: ReadCompactionModel(document.CompactionModel),
            microCompaction: ReadMicroCompaction(document.MicroCompaction),
            ruleFiles: ReadRuleFiles(document.RuleFiles),
            reservedOutputTokens: document.ReservedOutputTokens,
            agents: document.Agents,
            persistentDisabledBuiltInTools: ReadDisabledBuiltInTools(document.DisabledBuiltInTools),
            mcp: document.Mcp ?? defaults.Mcp,
            mcpToolApproval: mcpToolApproval);
    }

    public void Save(HarnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _home.EnsureCreated();
        var previous = File.Exists(_home.ConfigPath)
            ? JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(_home.ConfigPath), HomeJson.Options)
            : null;
        if (ProviderFiles.Discover(_home).Count == 0
            && previous?.Providers is { ValueKind: JsonValueKind.Object } legacyProviders)
        {
            _ = SettingsMapper.ReadProviders(legacyProviders);
            CopyProviders(legacyProviders);
        }

        var document = new SettingsDocument
        {
            Agents = settings.Agents == new AgentSettings() ? null : settings.Agents,
            Provider = settings.Provider.Value,
            Model = settings.Model,
            Approval = settings.Approval.Value,
            ThinkingEffort = settings.ThinkingEffort == ThinkingSelection.Default
                ? null
                : settings.ThinkingEffort.Value,
            ContextSelections = settings.ContextSelections.Count == 0
                ? null
                : SettingsMapper.WriteContextSelections(settings.ContextSelections),
            TierSelections = settings.TierSelections.Count == 0
                ? null
                : SettingsMapper.WriteTokenSelections(settings.TierSelections),
            SpeedSelections = settings.SpeedSelections.Count == 0
                ? null
                : SettingsMapper.WriteTokenSelections(settings.SpeedSelections),
            Skills = settings.Skills ? null : false,
            ExternalTools = settings.ExternalTools ? null : false,
            Mcp = settings.Mcp ? null : false,
            Plugins = settings.Plugins ? null : false,
            WorkspaceTrust = settings.WorkspaceTrust ? null : false,
            ExternalToolApproval = WriteExternalToolApproval(settings.ExternalToolApproval),
            McpToolApproval = WriteMcpToolApproval(settings.McpToolApproval),
            EstimatedTokens = settings.EstimatedTokens ? true : null,
            VerboseTools = settings.VerboseTools ? null : false,
            VerboseCommands = settings.VerboseCommands ? null : false,
            VerboseResults = settings.VerboseResults ? null : false,
            VerboseCalls = settings.VerboseCalls ? null : false,
            VerboseApprovals = settings.VerboseApprovals ? null : false,
            VerboseReview = settings.VerboseReview ? null : false,
            VerboseThinking = settings.VerboseThinking ? null : false,
            PromptSet = null,
            PromptAttachments = null,
            ExportDirectory = settings.ExportDirectory,
            CustomStatusLine = settings.StatusLine.Enabled ? true : null,
            StatusLine = settings.StatusLine.Fields.SequenceEqual(
                StatusLineSettings.DefaultFields,
                StringComparer.Ordinal)
                    ? null
                    : [.. settings.StatusLine.Fields],
            CompactionThreshold = settings.CompactionThreshold,
            ReservedOutputTokens = settings.ReservedOutputTokens,
            ShowCompactionSummary = settings.ShowCompactionSummary ? null : false,
            ShowAutomaticCompactionSummary = settings.ShowAutomaticCompactionSummary ? true : null,
            ExecutionBudget = ExecutionBudgetMapper.Write(settings.ExecutionBudget),
            BashTimeoutSeconds = BashTimeoutMapper.Write(
                settings.BashTimeoutSeconds,
                WorkspaceLimits.BashTimeoutSeconds),
            BackgroundBashTimeoutSeconds = BashTimeoutMapper.Write(
                settings.BackgroundBashTimeoutSeconds,
                WorkspaceLimits.BackgroundBashTimeoutSeconds),
            Providers = previous?.Providers,
            ApprovalModel = WriteApprovalModel(settings.ApprovalModel),
            CompactionModel = WriteCompactionModel(settings.CompactionModel),
            MicroCompaction = WriteMicroCompaction(settings.MicroCompaction),
            RuleFiles = WriteRuleFiles(settings.RuleFiles),
            ImageModel = WriteImageModel(settings.ImageModel),
            Retry = WriteRetry(settings.Retry),
            ApprovalRetry = WriteRetry(settings.ApprovalRetry),
            DisabledBuiltInTools = settings.PersistentDisabledBuiltInTools.Plan.Count == 0
                && settings.PersistentDisabledBuiltInTools.Work.Count == 0
                ? null
                : JsonSerializer.SerializeToElement(
                    new BuiltInToolDisablementDocument
                    {
                        Plan = [.. settings.PersistentDisabledBuiltInTools.Plan],
                        Work = [.. settings.PersistentDisabledBuiltInTools.Work]
                    },
                    HomeJson.Options)
        };
        var json = JsonSerializer.Serialize(document, HomeJson.Options);
        File.WriteAllText(_home.ConfigPath, json);
    }

    private IReadOnlyList<ProviderDefinition> ReadProviders(SettingsDocument document)
    {
        var files = ProviderFiles.Discover(_home);
        return files.Count == 0
            ? SettingsMapper.ReadProviders(document.Providers)
            : ProviderFiles.Read(_home, files);
    }

    private static BuiltInToolDisablement? ReadDisabledBuiltInTools(JsonElement? value)
    {
        if (value is not { } element || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var names = element.Deserialize<List<string>>(HomeJson.Options);
            return names is null ? null : new BuiltInToolDisablement(names, names);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("disabledBuiltInTools must be an array or an object with plan and work arrays.");
        }

        var document = element.Deserialize<BuiltInToolDisablementDocument>(HomeJson.Options)
            ?? throw new InvalidOperationException("disabledBuiltInTools is invalid.");
        return new BuiltInToolDisablement(document.Plan, document.Work);
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

        var thinkingEffort = string.IsNullOrWhiteSpace(document.ThinkingEffort)
            ? ThinkingSelection.Default
            : ThinkingSelection.Parse(document.ThinkingEffort);
        return new ApprovalModelSettings(
            document.Enabled ?? false,
            provider,
            model,
            thinkingEffort);
    }

    private static ApprovalModelDocument? WriteApprovalModel(ApprovalModelSettings settings)
    {
        if (!settings.Enabled
            && !settings.HasSelection
            && settings.ThinkingEffort == ThinkingSelection.Default)
        {
            return null;
        }

        return new ApprovalModelDocument
        {
            Enabled = settings.Enabled ? true : null,
            Provider = settings.Provider,
            Model = settings.Model,
            ThinkingEffort = settings.ThinkingEffort == ThinkingSelection.Default
                ? null
                : settings.ThinkingEffort.Value
        };
    }

    private static CompactionModelSettings ReadCompactionModel(CompactionModelDocument? document)
    {
        if (document is null)
        {
            return CompactionModelSettings.Off;
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
                "compactionModel.enabled requires provider and model.");
        }

        var thinkingEffort = string.IsNullOrWhiteSpace(document.ThinkingEffort)
            ? ThinkingSelection.Default
            : ThinkingSelection.Parse(document.ThinkingEffort);
        return new CompactionModelSettings(
            document.Enabled ?? false,
            provider,
            model,
            thinkingEffort);
    }

    private static CompactionModelDocument? WriteCompactionModel(CompactionModelSettings settings)
    {
        if (!settings.Enabled
            && !settings.HasSelection
            && settings.ThinkingEffort == ThinkingSelection.Default)
        {
            return null;
        }

        return new CompactionModelDocument
        {
            Enabled = settings.Enabled ? true : null,
            Provider = settings.Provider,
            Model = settings.Model,
            ThinkingEffort = settings.ThinkingEffort == ThinkingSelection.Default
                ? null
                : settings.ThinkingEffort.Value
        };
    }

    private static MicroCompactionSettings ReadMicroCompaction(MicroCompactionDocument? document)
    {
        if (document is null)
        {
            return MicroCompactionSettings.Default;
        }

        return new MicroCompactionSettings(
            document.Enabled ?? false,
            document.ProtectTurns ?? MicroCompactionSettings.DefaultProtectTurns,
            document.ProtectTokens ?? MicroCompactionSettings.DefaultProtectTokens,
            document.MinimumTokens ?? MicroCompactionSettings.DefaultMinimumTokens);
    }

    private static MicroCompactionDocument? WriteMicroCompaction(MicroCompactionSettings settings)
    {
        if (settings.IsDefault)
        {
            return null;
        }

        return new MicroCompactionDocument
        {
            Enabled = settings.Enabled ? true : null,
            ProtectTurns = settings.ProtectTurns == MicroCompactionSettings.DefaultProtectTurns
                ? null
                : settings.ProtectTurns,
            ProtectTokens = settings.ProtectTokens == MicroCompactionSettings.DefaultProtectTokens
                ? null
                : settings.ProtectTokens,
            MinimumTokens = settings.MinimumTokens == MicroCompactionSettings.DefaultMinimumTokens
                ? null
                : settings.MinimumTokens
        };
    }

    private static ImageModelSettings ReadImageModel(ImageModelDocument? document)
    {
        if (document is null)
        {
            return ImageModelSettings.None;
        }

        var provider = string.IsNullOrWhiteSpace(document.Provider)
            ? null
            : document.Provider.Trim();
        var model = string.IsNullOrWhiteSpace(document.Model)
            ? null
            : document.Model.Trim();
        if (provider is null || model is null)
        {
            throw new InvalidOperationException("imageModel requires provider and model.");
        }

        var thinkingEffort = string.IsNullOrWhiteSpace(document.ThinkingEffort)
            ? ThinkingSelection.Default
            : ThinkingSelection.Parse(document.ThinkingEffort);
        return new ImageModelSettings(provider, model, thinkingEffort);
    }

    private static ImageModelDocument? WriteImageModel(ImageModelSettings settings)
    {
        if (!settings.IsConfigured)
        {
            return null;
        }

        return new ImageModelDocument
        {
            Provider = settings.Provider,
            Model = settings.Model,
            ThinkingEffort = settings.ThinkingEffort == ThinkingSelection.Default
                ? null
                : settings.ThinkingEffort.Value
        };
    }

    private static RetrySettings ReadRetry(RetryDocument? document)
    {
        if (document is null)
        {
            return RetrySettings.Default;
        }

        return new RetrySettings(
            document.Enabled ?? true,
            document.MaximumRetries ?? SessionRetryOptions.DefaultMaximumRetries,
            document.InitialDelaySeconds ?? SessionRetryOptions.DefaultInitialDelaySeconds,
            document.BackoffFactor ?? SessionRetryOptions.DefaultBackoffFactor,
            document.JitterFactor ?? SessionRetryOptions.DefaultJitterFactor,
            document.MaximumDelaySeconds ?? SessionRetryOptions.DefaultMaximumDelaySeconds);
    }

    private static RetryDocument? WriteRetry(RetrySettings settings)
    {
        if (settings == RetrySettings.Default)
        {
            return null;
        }

        return new RetryDocument
        {
            Enabled = settings.Enabled ? null : false,
            MaximumRetries = settings.MaximumRetries == SessionRetryOptions.DefaultMaximumRetries
                ? null
                : settings.MaximumRetries,
            InitialDelaySeconds = settings.InitialDelaySeconds
                == SessionRetryOptions.DefaultInitialDelaySeconds
                    ? null
                    : settings.InitialDelaySeconds,
            BackoffFactor = settings.BackoffFactor == SessionRetryOptions.DefaultBackoffFactor
                ? null
                : settings.BackoffFactor,
            JitterFactor = settings.JitterFactor == SessionRetryOptions.DefaultJitterFactor
                ? null
                : settings.JitterFactor,
            MaximumDelaySeconds = settings.MaximumDelaySeconds
                == SessionRetryOptions.DefaultMaximumDelaySeconds
                    ? null
                    : settings.MaximumDelaySeconds
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

    private static McpToolApprovalSettings ReadMcpToolApproval(
        McpToolApprovalDocument? document,
        McpToolApprovalSettings defaults)
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
        return new McpToolApprovalSettings(home, project);
    }

    private static McpToolApprovalDocument? WriteMcpToolApproval(
        McpToolApprovalSettings settings)
    {
        if (settings == McpToolApprovalSettings.Default)
        {
            return null;
        }

        return new McpToolApprovalDocument
        {
            Home = settings.Home == McpToolApprovalSettings.Default.Home
                ? null
                : settings.Home.Value,
            Project = settings.Project == McpToolApprovalSettings.Default.Project
                ? null
                : settings.Project.Value
        };
    }

    private static RuleFileSettings ReadRuleFiles(RuleFilesDocument? document)
    {
        if (document is null)
        {
            return RuleFileSettings.Default;
        }

        return new RuleFileSettings(
            document.Global ?? RuleFileSettings.Default.Global,
            document.Project ?? RuleFileSettings.Default.Project);
    }

    private static RuleFilesDocument? WriteRuleFiles(RuleFileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsDefault)
        {
            return null;
        }

        return new RuleFilesDocument
        {
            Global = settings.Global ? null : false,
            Project = settings.Project ? null : false
        };
    }
}
