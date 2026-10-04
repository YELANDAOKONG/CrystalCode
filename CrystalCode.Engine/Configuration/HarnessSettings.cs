using Crystal.Reasoning;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Loaded host settings: selected provider and model plus the provider catalog.
/// </summary>
public sealed record HarnessSettings
{
    public const double DefaultCompactionThreshold = 0.8;

    public const bool DefaultSkills = true;

    public const bool DefaultExternalTools = true;

    public const bool DefaultEstimatedTokens = false;

    public const bool DefaultVerboseTools = true;

    public const bool DefaultVerboseCommands = true;

    public const bool DefaultVerboseApprovals = true;

    public const string DefaultPromptSet = "default";

    public HarnessSettings(
        ProviderName provider,
        string model,
        ApprovalMode approval,
        double compactionThreshold,
        ProviderCatalog catalog,
        ThinkingSelection? thinkingEffort = null,
        bool skills = DefaultSkills,
        bool externalTools = DefaultExternalTools,
        bool estimatedTokens = DefaultEstimatedTokens,
        bool verboseTools = DefaultVerboseTools,
        bool verboseCommands = DefaultVerboseCommands,
        bool verboseApprovals = DefaultVerboseApprovals,
        string promptSet = DefaultPromptSet,
        ExternalToolApprovalSettings? externalToolApproval = null,
        string? exportDirectory = null,
        StatusLineSettings? statusLine = null,
        TurnLimits? executionBudget = null,
        int? bashTimeoutSeconds = WorkspaceLimits.BashTimeoutSeconds,
        ApprovalModelSettings? approvalModel = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptSet);

        if (compactionThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compactionThreshold),
                compactionThreshold,
                "Compaction threshold must be greater than 0 and at most 1.");
        }

        if (!BashTool.IsSupported(bashTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bashTimeoutSeconds),
                bashTimeoutSeconds,
                "Bash timeout must be a positive supported number of seconds, or unlimited.");
        }

        _ = catalog.GetModel(provider, model);

        Provider = provider;
        Model = model.Trim();
        Approval = approval;
        CompactionThreshold = compactionThreshold;
        Catalog = catalog;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        Skills = skills;
        ExternalTools = externalTools;
        EstimatedTokens = estimatedTokens;
        VerboseTools = verboseTools;
        VerboseCommands = verboseCommands;
        VerboseApprovals = verboseApprovals;
        PromptSet = promptSet.Trim();
        ExternalToolApproval = externalToolApproval ?? ExternalToolApprovalSettings.Default;
        ExportDirectory = string.IsNullOrWhiteSpace(exportDirectory) ? null : exportDirectory.Trim();
        StatusLine = statusLine ?? new StatusLineSettings();
        ExecutionBudget = executionBudget ?? TurnLimits.CreateDefault();
        BashTimeoutSeconds = bashTimeoutSeconds;
        ApprovalModel = approvalModel ?? ApprovalModelSettings.Off;
        if (ApprovalModel.Enabled)
        {
            try
            {
                _ = catalog.GetModel(
                    new ProviderName(ApprovalModel.Provider!),
                    ApprovalModel.Model!);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }
        }
    }

    public ProviderName Provider { get; }

    public string Model { get; }

    public ApprovalMode Approval { get; }

    public double CompactionThreshold { get; }

    public ProviderCatalog Catalog { get; }

    public ThinkingSelection ThinkingEffort { get; }

    public bool Skills { get; }

    public bool ExternalTools { get; }

    public bool EstimatedTokens { get; }

    public bool VerboseTools { get; }

    public bool VerboseCommands { get; }

    public bool VerboseApprovals { get; }

    public string PromptSet { get; }

    public ExternalToolApprovalSettings ExternalToolApproval { get; }

    public string? ExportDirectory { get; }

    public StatusLineSettings StatusLine { get; }

    public TurnLimits ExecutionBudget { get; }

    public int? BashTimeoutSeconds { get; }

    public ApprovalModelSettings ApprovalModel { get; }

    public ProviderDefinition ActiveProvider => Catalog.GetModelProvider(Provider, Model);

    public ModelSettings ActiveModel => Catalog.GetModel(Provider, Model);

    public ReasoningOptions? ResolveReasoning() =>
        ThinkingEffort.ToReasoningOptions(ActiveModel);

    public static HarnessSettings CreateDefault()
    {
        var catalog = ProviderCatalog.CreateStarter();
        return new HarnessSettings(
            ProviderName.DeepSeek,
            "deepseek-flash",
            ApprovalMode.Default,
            DefaultCompactionThreshold,
            catalog);
    }

    public HarnessSettings WithOverrides(string? provider, string? model)
    {
        var nextProvider = string.IsNullOrWhiteSpace(provider)
            ? Provider
            : ProviderName.Parse(provider);
        var nextModel = string.IsNullOrWhiteSpace(model)
            ? ResolveModel(nextProvider)
            : model.Trim();

        return Copy(provider: nextProvider, model: nextModel);
    }

    public HarnessSettings WithSelection(ProviderName provider, string model)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return Copy(provider: provider, model: model.Trim());
    }

    public HarnessSettings WithApproval(ApprovalMode approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return Copy(approval: approval);
    }

    public HarnessSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return Copy(thinkingEffort: thinkingEffort);
    }

    public HarnessSettings WithSkills(bool skills) => Copy(skills: skills);

    public HarnessSettings WithExternalTools(bool externalTools) =>
        Copy(externalTools: externalTools);

    public HarnessSettings WithEstimatedTokens(bool estimatedTokens) =>
        Copy(estimatedTokens: estimatedTokens);

    public HarnessSettings WithVerboseTools(bool verboseTools) =>
        Copy(verboseTools: verboseTools);

    public HarnessSettings WithVerboseCommands(bool verboseCommands) =>
        Copy(verboseCommands: verboseCommands);

    public HarnessSettings WithVerboseApprovals(bool verboseApprovals) =>
        Copy(verboseApprovals: verboseApprovals);

    public HarnessSettings WithPromptSet(string promptSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptSet);
        return Copy(promptSet: promptSet.Trim());
    }

    public HarnessSettings WithExternalToolApproval(ExternalToolApprovalSettings approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return Copy(externalToolApproval: approval);
    }

    public HarnessSettings WithExportDirectory(string? exportDirectory) =>
        Copy(
            exportDirectory: string.IsNullOrWhiteSpace(exportDirectory)
                ? null
                : exportDirectory.Trim(),
            setExportDirectory: true);

    public HarnessSettings WithStatusLine(StatusLineSettings statusLine)
    {
        ArgumentNullException.ThrowIfNull(statusLine);
        return Copy(statusLine: statusLine);
    }

    public HarnessSettings WithExecutionBudget(TurnLimits executionBudget)
    {
        ArgumentNullException.ThrowIfNull(executionBudget);
        return Copy(executionBudget: executionBudget);
    }

    public HarnessSettings WithBashTimeout(int? bashTimeoutSeconds) =>
        Copy(bashTimeoutSeconds: bashTimeoutSeconds, setBashTimeout: true);

    public HarnessSettings WithApprovalModel(ApprovalModelSettings approvalModel)
    {
        ArgumentNullException.ThrowIfNull(approvalModel);
        return Copy(approvalModel: approvalModel, setApprovalModel: true);
    }

    private HarnessSettings Copy(
        ProviderName? provider = null,
        string? model = null,
        ApprovalMode? approval = null,
        ThinkingSelection? thinkingEffort = null,
        bool? skills = null,
        bool? externalTools = null,
        bool? estimatedTokens = null,
        bool? verboseTools = null,
        bool? verboseCommands = null,
        bool? verboseApprovals = null,
        string? promptSet = null,
        ExternalToolApprovalSettings? externalToolApproval = null,
        string? exportDirectory = null,
        bool setExportDirectory = false,
        StatusLineSettings? statusLine = null,
        TurnLimits? executionBudget = null,
        int? bashTimeoutSeconds = null,
        bool setBashTimeout = false,
        ApprovalModelSettings? approvalModel = null,
        bool setApprovalModel = false) =>
        new(
            provider ?? Provider,
            model ?? Model,
            approval ?? Approval,
            CompactionThreshold,
            Catalog,
            thinkingEffort ?? ThinkingEffort,
            skills ?? Skills,
            externalTools ?? ExternalTools,
            estimatedTokens ?? EstimatedTokens,
            verboseTools ?? VerboseTools,
            verboseCommands ?? VerboseCommands,
            verboseApprovals ?? VerboseApprovals,
            promptSet ?? PromptSet,
            externalToolApproval ?? ExternalToolApproval,
            setExportDirectory ? exportDirectory : ExportDirectory,
            statusLine ?? StatusLine,
            executionBudget ?? ExecutionBudget,
            setBashTimeout ? bashTimeoutSeconds : BashTimeoutSeconds,
            setApprovalModel ? approvalModel : ApprovalModel);

    public override string ToString() => nameof(HarnessSettings);

    private string ResolveModel(ProviderName provider)
    {
        if (provider == Provider && Catalog.GetModelNames(provider).Contains(Model, StringComparer.Ordinal))
        {
            return Model;
        }

        var models = Catalog.GetModelNames(provider);
        if (models.Count == 1)
        {
            return models[0];
        }

        if (models.Contains(Model, StringComparer.Ordinal))
        {
            return Model;
        }

        throw new InvalidOperationException(
            $"Provider '{provider.Value}' has more than one model. Pass --model.");
    }
}
