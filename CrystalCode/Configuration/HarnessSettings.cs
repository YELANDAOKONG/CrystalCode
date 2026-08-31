using Crystal.Reasoning;
using CrystalCode.Approvals;

namespace CrystalCode.Configuration;

/// <summary>
/// Loaded host settings: selected provider and model plus the provider catalog.
/// </summary>
public sealed record HarnessSettings
{
    public const double DefaultCompactionThreshold = 0.8;

    public const bool DefaultSkills = true;

    public HarnessSettings(
        ProviderName provider,
        string model,
        ApprovalMode approval,
        double compactionThreshold,
        ProviderCatalog catalog,
        ThinkingSelection? thinkingEffort = null,
        bool skills = DefaultSkills)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(catalog);

        if (compactionThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compactionThreshold),
                compactionThreshold,
                "Compaction threshold must be greater than 0 and at most 1.");
        }

        _ = catalog.GetModel(provider, model);

        Provider = provider;
        Model = model.Trim();
        Approval = approval;
        CompactionThreshold = compactionThreshold;
        Catalog = catalog;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        Skills = skills;
    }

    public ProviderName Provider { get; }

    public string Model { get; }

    public ApprovalMode Approval { get; }

    public double CompactionThreshold { get; }

    public ProviderCatalog Catalog { get; }

    public ThinkingSelection ThinkingEffort { get; }

    public bool Skills { get; }

    public ProviderDefinition ActiveProvider => Catalog.Get(Provider);

    public ModelSettings ActiveModel => Catalog.GetModel(Provider, Model);

    public ReasoningOptions? ResolveReasoning() =>
        ThinkingEffort.ToReasoningOptions(ActiveModel);

    public static HarnessSettings CreateDefault()
    {
        var catalog = ProviderCatalog.CreateStarter();
        return new HarnessSettings(
            ProviderName.DeepSeek,
            "deepseek-v4-flash",
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

    private HarnessSettings Copy(
        ProviderName? provider = null,
        string? model = null,
        ApprovalMode? approval = null,
        ThinkingSelection? thinkingEffort = null,
        bool? skills = null) =>
        new(
            provider ?? Provider,
            model ?? Model,
            approval ?? Approval,
            CompactionThreshold,
            Catalog,
            thinkingEffort ?? ThinkingEffort,
            skills ?? Skills);

    public override string ToString() => nameof(HarnessSettings);

    private string ResolveModel(ProviderName provider)
    {
        if (provider == Provider && Catalog.Get(provider).TryGetModel(Model, out _))
        {
            return Model;
        }

        var models = Catalog.Get(provider).Models;
        if (models.Count == 1)
        {
            return models.Keys.First();
        }

        if (models.ContainsKey(Model))
        {
            return Model;
        }

        throw new InvalidOperationException(
            $"Provider '{provider.Value}' has more than one model. Pass --model.");
    }
}
