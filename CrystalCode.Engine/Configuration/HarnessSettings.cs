using System.Collections.ObjectModel;

using Crystal.Reasoning;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Loaded host settings: selected provider and model plus the provider catalog.
/// </summary>
public sealed record HarnessSettings
{
    public const double DefaultCompactionThreshold = 0.8;

    public const bool DefaultShowCompactionSummary = true;

    public const bool DefaultShowAutomaticCompactionSummary = false;

    public const bool DefaultSkills = true;

    public const bool DefaultExternalTools = true;

    public const bool DefaultMcp = true;

    public const bool DefaultPlugins = true;

    public const bool DefaultWorkspaceTrust = true;

    public const bool DefaultEstimatedTokens = false;

    public const bool DefaultTodoBar = true;

    public const int MinimumTodoBarRows = 3;

    public const int MaximumTodoBarRows = 10;

    public const int DefaultTodoBarRows = 4;

    public const bool DefaultVerboseTools = true;

    public const bool DefaultVerboseCommands = true;

    public const bool DefaultVerboseResults = true;

    public const bool DefaultVerboseCalls = true;

    public const bool DefaultVerboseApprovals = true;

    public const bool DefaultVerboseReview = true;

    public const bool DefaultVerboseThinking = true;

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
        bool todoBar = DefaultTodoBar,
        int todoBarRows = DefaultTodoBarRows,
        bool verboseTools = DefaultVerboseTools,
        bool verboseCommands = DefaultVerboseCommands,
        bool verboseResults = DefaultVerboseResults,
        bool verboseCalls = DefaultVerboseCalls,
        bool verboseApprovals = DefaultVerboseApprovals,
        bool verboseReview = DefaultVerboseReview,
        bool verboseThinking = DefaultVerboseThinking,
        string promptSet = DefaultPromptSet,
        ExternalToolApprovalSettings? externalToolApproval = null,
        string? exportDirectory = null,
        StatusLineSettings? statusLine = null,
        TurnLimits? executionBudget = null,
        int? bashTimeoutSeconds = WorkspaceLimits.BashTimeoutSeconds,
        int? backgroundBashTimeoutSeconds = WorkspaceLimits.BackgroundBashTimeoutSeconds,
        ApprovalModelSettings? approvalModel = null,
        bool plugins = DefaultPlugins,
        bool workspaceTrust = DefaultWorkspaceTrust,
        bool showCompactionSummary = DefaultShowCompactionSummary,
        IReadOnlyList<string>? promptAttachments = null,
        string? promptSetOverride = null,
        bool usePromptAttachments = true,
        ImageModelSettings? imageModel = null,
        IReadOnlyDictionary<ContextSelectionKey, int>? contextSelections = null,
        RetrySettings? retry = null,
        RetrySettings? approvalRetry = null,
        CompactionModelSettings? compactionModel = null,
        bool showAutomaticCompactionSummary = DefaultShowAutomaticCompactionSummary,
        MicroCompactionSettings? microCompaction = null,
        RuleFileSettings? ruleFiles = null,
        IReadOnlyList<string>? disabledBuiltInTools = null,
        IReadOnlyDictionary<ContextSelectionKey, string>? tierSelections = null,
        IReadOnlyDictionary<ContextSelectionKey, string>? speedSelections = null,
        int? reservedOutputTokens = null,
        int? outputTokenCap = null,
        AgentSettings? agents = null,
        BuiltInToolDisablement? persistentDisabledBuiltInTools = null,
        BuiltInToolDisablement? sessionDisabledBuiltInTools = null,
        bool mcp = DefaultMcp,
        McpToolApprovalSettings? mcpToolApproval = null,
        ExtendedApprovalSettings? extendedApproval = null)
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

        if (todoBarRows is < MinimumTodoBarRows or > MaximumTodoBarRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(todoBarRows),
                todoBarRows,
                $"Todo bar rows must be between {MinimumTodoBarRows} and {MaximumTodoBarRows}.");
        }

        if (reservedOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedOutputTokens),
                reservedOutputTokens,
                "Reserved output tokens must be positive.");
        }

        if (outputTokenCap is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputTokenCap),
                outputTokenCap,
                "Output token cap must be positive.");
        }

        if (!BashTool.IsSupported(bashTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bashTimeoutSeconds),
                bashTimeoutSeconds,
                "Bash timeout must be a positive supported number of seconds, or unlimited.");
        }

        if (!BashTool.IsSupported(backgroundBashTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(backgroundBashTimeoutSeconds),
                backgroundBashTimeoutSeconds,
                "Background bash timeout must be a positive supported number of seconds, or unlimited.");
        }

        _ = catalog.GetModel(provider, model);

        Provider = provider;
        Model = model.Trim();
        Approval = approval;
        CompactionThreshold = compactionThreshold;
        ReservedOutputTokens = reservedOutputTokens;
        OutputTokenCap = outputTokenCap;
        Agents = agents ?? new AgentSettings();
        Catalog = catalog;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        ContextSelections = new ReadOnlyDictionary<ContextSelectionKey, int>(
            contextSelections is null
                ? new Dictionary<ContextSelectionKey, int>()
                : contextSelections.ToDictionary(pair => pair.Key, pair => pair.Value));
        TierSelections = CopyTokens(tierSelections);
        SpeedSelections = CopyTokens(speedSelections);
        Retry = retry ?? RetrySettings.Default;
        ApprovalRetry = approvalRetry ?? RetrySettings.Default;
        Skills = skills;
        ExternalTools = externalTools;
        Mcp = mcp;
        McpToolApproval = mcpToolApproval ?? McpToolApprovalSettings.Default;
        EstimatedTokens = estimatedTokens;
        TodoBar = todoBar;
        TodoBarRows = todoBarRows;
        VerboseTools = verboseTools;
        VerboseCommands = verboseCommands;
        VerboseResults = verboseResults;
        VerboseCalls = verboseCalls;
        VerboseApprovals = verboseApprovals;
        VerboseReview = verboseReview;
        VerboseThinking = verboseThinking;
        PromptSet = promptSet.Trim();
        ExternalToolApproval = externalToolApproval ?? ExternalToolApprovalSettings.Default;
        ExportDirectory = string.IsNullOrWhiteSpace(exportDirectory) ? null : exportDirectory.Trim();
        StatusLine = statusLine ?? new StatusLineSettings();
        ExecutionBudget = executionBudget ?? TurnLimits.CreateDefault();
        BashTimeoutSeconds = bashTimeoutSeconds;
        BackgroundBashTimeoutSeconds = backgroundBashTimeoutSeconds;
        ApprovalModel = approvalModel ?? ApprovalModelSettings.Off;
        ExtendedApproval = extendedApproval ?? ExtendedApprovalSettings.Off;
        CompactionModel = compactionModel ?? CompactionModelSettings.Off;
        MicroCompaction = microCompaction ?? MicroCompactionSettings.Default;
        RuleFiles = ruleFiles ?? RuleFileSettings.Default;
        ProcessDisabledBuiltInTools = BuiltInToolDisablement.FromProcessNames(disabledBuiltInTools);
        PersistentDisabledBuiltInTools = persistentDisabledBuiltInTools ?? new BuiltInToolDisablement();
        SessionDisabledBuiltInTools = sessionDisabledBuiltInTools ?? new BuiltInToolDisablement();
        DisabledBuiltInTools = BuiltInToolDisablement.Merge(
            BuiltInToolDisablement.Merge(PersistentDisabledBuiltInTools, ProcessDisabledBuiltInTools),
            SessionDisabledBuiltInTools);
        ImageModel = imageModel ?? ImageModelSettings.None;
        Plugins = plugins;
        WorkspaceTrust = workspaceTrust;
        ShowCompactionSummary = showCompactionSummary;
        ShowAutomaticCompactionSummary = showAutomaticCompactionSummary;
        PromptAttachments = CopyPromptAttachments(promptAttachments);
        PromptSetOverride = string.IsNullOrWhiteSpace(promptSetOverride)
            ? null
            : promptSetOverride.Trim();
        UsePromptAttachments = usePromptAttachments;
        RequireReviewer(catalog, ApprovalModel, "Approval model");
        RequireReviewer(catalog, ExtendedApproval.Reviewer, "Extended approval model");

        if (CompactionModel.Enabled)
        {
            try
            {
                _ = catalog.GetModel(
                    new ProviderName(CompactionModel.Provider!),
                    CompactionModel.Model!);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }
        }

        RequireImageModel(catalog, ImageModel);
    }

    public ProviderName Provider { get; }

    public string Model { get; }

    public ApprovalMode Approval { get; }

    public double CompactionThreshold { get; }

    /// <summary>
    /// Declared tokens to hold back for the reply, or null to derive them from
    /// the window and the model output cap.
    /// </summary>
    public int? ReservedOutputTokens { get; }

    /// <summary>
    /// Output tokens a client built from these settings asks for, when set.
    /// Null keeps the fitted turn cap. This is not saved.
    /// </summary>
    public int? OutputTokenCap { get; }

    public ProviderCatalog Catalog { get; }

    public ThinkingSelection ThinkingEffort { get; }

    public IReadOnlyDictionary<ContextSelectionKey, int> ContextSelections { get; }

    public IReadOnlyDictionary<ContextSelectionKey, string> TierSelections { get; }

    public IReadOnlyDictionary<ContextSelectionKey, string> SpeedSelections { get; }

    /// <summary>
    /// Service-tier token for the active model, or null when unset or no longer listed.
    /// </summary>
    public string? ServiceTier => ResolveSlot(Provider, Model, ActiveModel.ServiceTiers, TierSelections);

    /// <summary>
    /// Speed token for the active model, or null when unset or no longer listed.
    /// </summary>
    public string? Speed => ResolveSlot(Provider, Model, ActiveModel.Speeds, SpeedSelections);

    public RetrySettings Retry { get; }

    public RetrySettings ApprovalRetry { get; }

    public AgentSettings Agents { get; }

    public int ContextWindow =>
        ContextSelections.TryGetValue(new ContextSelectionKey(Provider, Model), out var selected)
        && ActiveModel.ContextWindows.Contains(selected)
            ? selected
            : ActiveModel.ContextWindows[0];

    /// <summary>
    /// Output cap for one request, fitted to the room the window leaves after
    /// the compaction ceiling. Null when the model declares no cap, which
    /// leaves the provider default in place.
    /// </summary>
    public int? MaxOutputTokens
    {
        get
        {
            if (OutputTokenCap is int cap)
            {
                return cap;
            }

            var tokens = ContextAccountant.OutputTokens(
                ContextWindow,
                CompactionThreshold,
                ActiveModel.MaxTokens,
                ReservedOutputTokens);
            return tokens > 0 ? tokens : null;
        }
    }

    public HarnessSettings WithContextWindow(ProviderName provider, string model, int window)
    {
        if (!Catalog.GetModel(provider, model).ContextWindows.Contains(window))
        {
            throw new ArgumentOutOfRangeException(nameof(window), window,
                "Context window is not available for this model.");
        }

        var selections = ContextSelections.ToDictionary(pair => pair.Key, pair => pair.Value);
        selections[new ContextSelectionKey(provider, model)] = window;
        return Copy(contextSelections: selections);
    }

    /// <summary>
    /// Selects one declared budget of the current model for this process.
    /// The caller keeps this copy off the persisted baseline. A model with
    /// one budget is refused, because there is no other tier to select.
    /// </summary>
    public HarnessSettings WithContextBudget(string budget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(budget);
        var windows = ActiveModel.ContextWindows;
        if (windows.Count < 2)
        {
            throw new ArgumentException("The selected model has one context budget.");
        }

        if (!ContextWindowSelection.TryParse(budget, out var selected) || !windows.Contains(selected))
        {
            throw new ArgumentException(
                "Context window is not available for this model. Available: "
                + string.Join(", ", windows.Select(ContextWindowSelection.Format)));
        }

        return WithContextWindow(Provider, Model, selected);
    }

    public HarnessSettings WithTier(ProviderName provider, string model, string? token) =>
        WithSlot(provider, model, token, serviceTier: true);

    public HarnessSettings WithSpeed(ProviderName provider, string model, string? token) =>
        WithSlot(provider, model, token, serviceTier: false);

    public string? TierFor(ProviderName provider, string model) =>
        ResolveSlot(provider, model, Catalog.GetModel(provider, model).ServiceTiers, TierSelections);

    public string? SpeedFor(ProviderName provider, string model) =>
        ResolveSlot(provider, model, Catalog.GetModel(provider, model).Speeds, SpeedSelections);

    public bool Skills { get; }

    public bool ExternalTools { get; }

    public bool Mcp { get; }

    public bool Plugins { get; }

    public bool WorkspaceTrust { get; }

    public RuleFileSettings RuleFiles { get; }

    public bool ShowCompactionSummary { get; }

    public bool ShowAutomaticCompactionSummary { get; }

    public bool EstimatedTokens { get; }

    /// <summary>
    /// Whether the pinned todo list is painted above the status bar.
    /// </summary>
    public bool TodoBar { get; }

    /// <summary>
    /// Items the pinned todo list shows before it folds the rest into a
    /// <c>+N more</c> line.
    /// </summary>
    public int TodoBarRows { get; }

    public bool VerboseTools { get; }

    public bool VerboseCommands { get; }

    public bool VerboseResults { get; }

    public bool VerboseCalls { get; }

    public bool VerboseApprovals { get; }

    public bool VerboseReview { get; }

    public bool VerboseThinking { get; }

    public string PromptSet { get; }

    /// <summary>
    /// Process-only attachment names appended after the enabled set, even when
    /// a manifest says <c>enabled</c> false. Empty follows the files only.
    /// Ignored when <see cref="UsePromptAttachments"/> is false. Not written
    /// to <c>config.json</c>.
    /// </summary>
    public IReadOnlyList<string> PromptAttachments { get; }

    /// <summary>
    /// Process-only prompt set. Null follows <c>prompt.json</c>. This value
    /// is not written to <c>config.json</c>.
    /// </summary>
    public string? PromptSetOverride { get; }

    /// <summary>
    /// Process-only attachment switch. False appends nothing, including
    /// <see cref="PromptAttachments"/>. This value is not written to
    /// <c>config.json</c>.
    /// </summary>
    public bool UsePromptAttachments { get; }

    /// <summary>
    /// Built-in tool names omitted from the active catalogs: Home preferences,
    /// process-only launch settings, and the current session combined. Empty
    /// keeps the full catalog. This value is not written to <c>config.json</c>.
    /// </summary>
    public BuiltInToolDisablement DisabledBuiltInTools { get; }

    /// <summary>
    /// Built-in tool names disabled in Home preferences. This list is written
    /// to <c>config.json</c>; process-only launch overrides are not included.
    /// </summary>
    public BuiltInToolDisablement PersistentDisabledBuiltInTools { get; }

    /// <summary>
    /// Built-in tool names disabled by process-only launch settings.
    /// </summary>
    public BuiltInToolDisablement ProcessDisabledBuiltInTools { get; }

    /// <summary>
    /// Built-in tool names disabled for the current session only. They are
    /// saved in the session file, not in <c>config.json</c>.
    /// </summary>
    public BuiltInToolDisablement SessionDisabledBuiltInTools { get; }

    public ExternalToolApprovalSettings ExternalToolApproval { get; }

    public McpToolApprovalSettings McpToolApproval { get; }

    public string? ExportDirectory { get; }

    public StatusLineSettings StatusLine { get; }

    public TurnLimits ExecutionBudget { get; }

    public int? BashTimeoutSeconds { get; }

    public int? BackgroundBashTimeoutSeconds { get; }

    public ApprovalModelSettings ApprovalModel { get; }

    public ExtendedApprovalSettings ExtendedApproval { get; }

    public CompactionModelSettings CompactionModel { get; }

    public MicroCompactionSettings MicroCompaction { get; }

    public ImageModelSettings ImageModel { get; }

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

    public HarnessSettings WithAgents(AgentSettings agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        return Copy(agents: agents);
    }

    public HarnessSettings WithSkills(bool skills) => Copy(skills: skills);

    public HarnessSettings WithExternalTools(bool externalTools) =>
        Copy(externalTools: externalTools);

    public HarnessSettings WithMcp(bool mcp) => Copy(mcp: mcp);

    public HarnessSettings WithPlugins(bool plugins) => Copy(plugins: plugins);

    public HarnessSettings WithWorkspaceTrust(bool workspaceTrust) =>
        Copy(workspaceTrust: workspaceTrust);

    public HarnessSettings WithRuleFiles(RuleFileSettings ruleFiles)
    {
        ArgumentNullException.ThrowIfNull(ruleFiles);
        return Copy(ruleFiles: ruleFiles);
    }

    public HarnessSettings WithShowCompactionSummary(bool showCompactionSummary) =>
        Copy(showCompactionSummary: showCompactionSummary);

    /// <summary>
    /// Sets the tokens held back for the reply, or clears them so the window
    /// and the model output cap decide.
    /// </summary>
    public HarnessSettings WithReservedOutputTokens(int? reservedOutputTokens) =>
        Copy(reservedOutputTokens: reservedOutputTokens, setReservedOutputTokens: true);

    /// <summary>
    /// Forces the output cap on a client built from the copy. The summary
    /// client uses this because a chat request cannot carry its own cap.
    /// </summary>
    public HarnessSettings WithOutputTokenCap(int outputTokenCap) =>
        Copy(outputTokenCap: outputTokenCap);

    public HarnessSettings WithShowAutomaticCompactionSummary(bool showAutomaticCompactionSummary) =>
        Copy(showAutomaticCompactionSummary: showAutomaticCompactionSummary);

    public HarnessSettings WithEstimatedTokens(bool estimatedTokens) =>
        Copy(estimatedTokens: estimatedTokens);

    public HarnessSettings WithTodoBar(bool todoBar) =>
        Copy(todoBar: todoBar);

    public HarnessSettings WithTodoBarRows(int todoBarRows) =>
        Copy(todoBarRows: todoBarRows);

    public HarnessSettings WithVerboseTools(bool verboseTools) =>
        Copy(verboseTools: verboseTools);

    public HarnessSettings WithVerboseCommands(bool verboseCommands) =>
        Copy(verboseCommands: verboseCommands);

    public HarnessSettings WithVerboseResults(bool verboseResults) =>
        Copy(verboseResults: verboseResults);

    public HarnessSettings WithVerboseCalls(bool verboseCalls) =>
        Copy(verboseCalls: verboseCalls);

    public HarnessSettings WithVerboseApprovals(bool verboseApprovals) =>
        Copy(verboseApprovals: verboseApprovals);

    public HarnessSettings WithVerboseReview(bool verboseReview) =>
        Copy(verboseReview: verboseReview);

    public HarnessSettings WithVerboseThinking(bool verboseThinking) =>
        Copy(verboseThinking: verboseThinking);

    public HarnessSettings WithPromptSet(string promptSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptSet);
        return Copy(promptSet: promptSet.Trim());
    }

    public HarnessSettings WithPromptAttachments(IReadOnlyList<string> promptAttachments)
    {
        ArgumentNullException.ThrowIfNull(promptAttachments);
        return Copy(promptAttachments: promptAttachments, setPromptAttachments: true);
    }

    public HarnessSettings WithPromptSetOverride(string promptSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptSet);
        return Copy(promptSetOverride: promptSet.Trim(), setPromptSetOverride: true);
    }

    public HarnessSettings WithUsePromptAttachments(bool usePromptAttachments) =>
        Copy(usePromptAttachments: usePromptAttachments, setUsePromptAttachments: true);

    public HarnessSettings WithDisabledBuiltInTools(IReadOnlyList<string> disabledBuiltInTools)
    {
        ArgumentNullException.ThrowIfNull(disabledBuiltInTools);
        return Copy(disabledBuiltInTools: disabledBuiltInTools);
    }

    public HarnessSettings WithPersistentDisabledBuiltInTools(
        BuiltInToolDisablement disabledBuiltInTools)
    {
        ArgumentNullException.ThrowIfNull(disabledBuiltInTools);
        return Copy(persistentDisabledBuiltInTools: disabledBuiltInTools);
    }

    public HarnessSettings WithSessionDisabledBuiltInTools(
        BuiltInToolDisablement disabledBuiltInTools)
    {
        ArgumentNullException.ThrowIfNull(disabledBuiltInTools);
        return Copy(sessionDisabledBuiltInTools: disabledBuiltInTools);
    }

    public HarnessSettings WithPersistentDisabledBuiltInTools(
        IReadOnlyList<string> disabledBuiltInTools)
    {
        ArgumentNullException.ThrowIfNull(disabledBuiltInTools);
        return WithPersistentDisabledBuiltInTools(
            BuiltInToolDisablement.FromProcessNames(disabledBuiltInTools));
    }

    public HarnessSettings WithExternalToolApproval(ExternalToolApprovalSettings approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return Copy(externalToolApproval: approval);
    }

    public HarnessSettings WithMcpToolApproval(McpToolApprovalSettings approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return Copy(mcpToolApproval: approval);
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

    public HarnessSettings WithBackgroundBashTimeout(int? backgroundBashTimeoutSeconds) =>
        Copy(
            backgroundBashTimeoutSeconds: backgroundBashTimeoutSeconds,
            setBackgroundBashTimeout: true);

    public HarnessSettings WithRetry(RetrySettings retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        return Copy(retry: retry);
    }

    public HarnessSettings WithApprovalRetry(RetrySettings approvalRetry)
    {
        ArgumentNullException.ThrowIfNull(approvalRetry);
        return Copy(approvalRetry: approvalRetry);
    }

    public HarnessSettings WithApprovalModel(ApprovalModelSettings approvalModel)
    {
        ArgumentNullException.ThrowIfNull(approvalModel);
        return Copy(approvalModel: approvalModel, setApprovalModel: true);
    }

    public HarnessSettings WithExtendedApproval(ExtendedApprovalSettings extendedApproval)
    {
        ArgumentNullException.ThrowIfNull(extendedApproval);
        return Copy(extendedApproval: extendedApproval);
    }

    /// <summary>
    /// Selects a reviewer's provider and model, then applies its own service
    /// tier and speed. An unset reviewer slot keeps the per-model selection.
    /// </summary>
    public HarnessSettings ForReviewer(ApprovalModelSettings reviewer)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        if (!reviewer.HasSelection)
        {
            throw new InvalidOperationException("The reviewer has no model selection.");
        }

        var provider = new ProviderName(reviewer.Provider!);
        var selected = WithSelection(provider, reviewer.Model!);
        if (reviewer.ServiceTier is not null)
        {
            selected = selected.WithTier(provider, reviewer.Model!, reviewer.ServiceTier);
        }

        if (reviewer.Speed is not null)
        {
            selected = selected.WithSpeed(provider, reviewer.Model!, reviewer.Speed);
        }

        return selected;
    }

    public HarnessSettings WithImageModel(ImageModelSettings imageModel)
    {
        ArgumentNullException.ThrowIfNull(imageModel);
        return Copy(imageModel: imageModel, setImageModel: true);
    }

    public HarnessSettings WithCompactionModel(CompactionModelSettings compactionModel)
    {
        ArgumentNullException.ThrowIfNull(compactionModel);
        return Copy(compactionModel: compactionModel, setCompactionModel: true);
    }

    public HarnessSettings WithMicroCompaction(MicroCompactionSettings microCompaction)
    {
        ArgumentNullException.ThrowIfNull(microCompaction);
        return Copy(microCompaction: microCompaction, setMicroCompaction: true);
    }

    private HarnessSettings Copy(
        ProviderName? provider = null,
        string? model = null,
        ApprovalMode? approval = null,
        ThinkingSelection? thinkingEffort = null,
        bool? skills = null,
        bool? externalTools = null,
        bool? estimatedTokens = null,
        bool? todoBar = null,
        int? todoBarRows = null,
        bool? verboseTools = null,
        bool? verboseCommands = null,
        bool? verboseResults = null,
        bool? verboseCalls = null,
        bool? verboseApprovals = null,
        bool? verboseReview = null,
        bool? verboseThinking = null,
        string? promptSet = null,
        ExternalToolApprovalSettings? externalToolApproval = null,
        string? exportDirectory = null,
        bool setExportDirectory = false,
        StatusLineSettings? statusLine = null,
        TurnLimits? executionBudget = null,
        int? bashTimeoutSeconds = null,
        bool setBashTimeout = false,
        int? backgroundBashTimeoutSeconds = null,
        bool setBackgroundBashTimeout = false,
        ApprovalModelSettings? approvalModel = null,
        bool setApprovalModel = false,
        bool? plugins = null,
        bool? workspaceTrust = null,
        bool? showCompactionSummary = null,
        IReadOnlyList<string>? promptAttachments = null,
        bool setPromptAttachments = false,
        string? promptSetOverride = null,
        bool setPromptSetOverride = false,
        bool? usePromptAttachments = null,
        bool setUsePromptAttachments = false,
        ImageModelSettings? imageModel = null,
        bool setImageModel = false,
        IReadOnlyDictionary<ContextSelectionKey, int>? contextSelections = null,
        RetrySettings? retry = null,
        RetrySettings? approvalRetry = null,
        CompactionModelSettings? compactionModel = null,
        bool setCompactionModel = false,
        bool? showAutomaticCompactionSummary = null,
        MicroCompactionSettings? microCompaction = null,
        bool setMicroCompaction = false,
        RuleFileSettings? ruleFiles = null,
        IReadOnlyList<string>? disabledBuiltInTools = null,
        BuiltInToolDisablement? persistentDisabledBuiltInTools = null,
        BuiltInToolDisablement? sessionDisabledBuiltInTools = null,
        IReadOnlyDictionary<ContextSelectionKey, string>? tierSelections = null,
        IReadOnlyDictionary<ContextSelectionKey, string>? speedSelections = null,
        int? reservedOutputTokens = null,
        bool setReservedOutputTokens = false,
        int? outputTokenCap = null,
        AgentSettings? agents = null,
        bool? mcp = null,
        McpToolApprovalSettings? mcpToolApproval = null,
        ExtendedApprovalSettings? extendedApproval = null) =>
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
            todoBar ?? TodoBar,
            todoBarRows ?? TodoBarRows,
            verboseTools ?? VerboseTools,
            verboseCommands ?? VerboseCommands,
            verboseResults ?? VerboseResults,
            verboseCalls ?? VerboseCalls,
            verboseApprovals ?? VerboseApprovals,
            verboseReview ?? VerboseReview,
            verboseThinking ?? VerboseThinking,
            promptSet ?? PromptSet,
            externalToolApproval ?? ExternalToolApproval,
            setExportDirectory ? exportDirectory : ExportDirectory,
            statusLine ?? StatusLine,
            executionBudget ?? ExecutionBudget,
            setBashTimeout ? bashTimeoutSeconds : BashTimeoutSeconds,
            setBackgroundBashTimeout ? backgroundBashTimeoutSeconds : BackgroundBashTimeoutSeconds,
            setApprovalModel ? approvalModel : ApprovalModel,
            plugins ?? Plugins,
            workspaceTrust ?? WorkspaceTrust,
            showCompactionSummary ?? ShowCompactionSummary,
            setPromptAttachments ? promptAttachments : PromptAttachments,
            setPromptSetOverride ? promptSetOverride : PromptSetOverride,
            setUsePromptAttachments ? usePromptAttachments ?? UsePromptAttachments : UsePromptAttachments,
            setImageModel ? imageModel : ImageModel,
            contextSelections ?? ContextSelections,
            retry ?? Retry,
            approvalRetry ?? ApprovalRetry,
            setCompactionModel ? compactionModel : CompactionModel,
            showAutomaticCompactionSummary ?? ShowAutomaticCompactionSummary,
            setMicroCompaction ? microCompaction : MicroCompaction,
            ruleFiles ?? RuleFiles,
            disabledBuiltInTools ?? ProcessDisabledBuiltInTools.All(),
            tierSelections ?? TierSelections,
            speedSelections ?? SpeedSelections,
            setReservedOutputTokens ? reservedOutputTokens : ReservedOutputTokens,
            outputTokenCap ?? OutputTokenCap,
            agents ?? Agents,
            persistentDisabledBuiltInTools ?? PersistentDisabledBuiltInTools,
            sessionDisabledBuiltInTools ?? SessionDisabledBuiltInTools,
            mcp ?? Mcp,
            mcpToolApproval ?? McpToolApproval,
            extendedApproval ?? ExtendedApproval);

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

    private HarnessSettings WithSlot(
        ProviderName provider,
        string model,
        string? token,
        bool serviceTier)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var settings = Catalog.GetModel(provider, model);
        var listed = serviceTier ? settings.ServiceTiers : settings.Speeds;
        var label = serviceTier ? "Service tier" : "Speed";
        string? normalized = null;
        if (token is not null)
        {
            if (listed.Count == 0 || !CatalogSlots.Contains(listed, token))
            {
                throw new ArgumentException(Unavailable(label, listed));
            }

            normalized = new CatalogToken(token).Value;
        }

        var source = serviceTier ? TierSelections : SpeedSelections;
        var selections = source.ToDictionary(pair => pair.Key, pair => pair.Value);
        var key = new ContextSelectionKey(provider, model);
        if (normalized is null)
        {
            selections.Remove(key);
        }
        else
        {
            selections[key] = normalized;
        }

        return serviceTier
            ? Copy(tierSelections: selections)
            : Copy(speedSelections: selections);
    }

    private static string? ResolveSlot(
        ProviderName provider,
        string model,
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<ContextSelectionKey, string> selections)
    {
        if (!selections.TryGetValue(new ContextSelectionKey(provider, model), out var stored))
        {
            return null;
        }

        return CatalogSlots.Contains(tokens, stored) ? stored : null;
    }

    private static string Unavailable(string label, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return label + " is not available for this model.";
        }

        return label + " is not available for this model. Available: " + string.Join(", ", tokens);
    }

    private static IReadOnlyDictionary<ContextSelectionKey, string> CopyTokens(
        IReadOnlyDictionary<ContextSelectionKey, string>? selections) =>
        new ReadOnlyDictionary<ContextSelectionKey, string>(
            selections is null
                ? new Dictionary<ContextSelectionKey, string>()
                : selections.ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>
    /// An enabled reviewer must name a catalog model, and its own tier and
    /// speed must be listed by that model. A disabled one is kept unchecked.
    /// </summary>
    private static void RequireReviewer(
        ProviderCatalog catalog,
        ApprovalModelSettings reviewer,
        string label)
    {
        if (!reviewer.Enabled)
        {
            return;
        }

        ModelSettings model;
        try
        {
            model = catalog.GetModel(new ProviderName(reviewer.Provider!), reviewer.Model!);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }

        if (reviewer.ServiceTier is not null && !CatalogSlots.Contains(model.ServiceTiers, reviewer.ServiceTier))
        {
            throw new InvalidOperationException(
                Unavailable(label + " service tier", model.ServiceTiers));
        }

        if (reviewer.Speed is not null && !CatalogSlots.Contains(model.Speeds, reviewer.Speed))
        {
            throw new InvalidOperationException(Unavailable(label + " speed", model.Speeds));
        }
    }

    private static void RequireImageModel(ProviderCatalog catalog, ImageModelSettings imageModel)
    {
        if (!imageModel.IsConfigured)
        {
            return;
        }

        ModelSettings model;
        try
        {
            model = catalog.GetModel(new ProviderName(imageModel.Provider!), imageModel.Model!);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }

        if (!model.ImageInput)
        {
            throw new InvalidOperationException(
                $"Image model '{imageModel.Model}' does not accept image input.");
        }
    }

    private static IReadOnlyList<string> CopyPromptAttachments(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return [];
        }

        var copy = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            copy.Add(name.Trim());
        }

        return copy;
    }
}
