using Crystal.Reasoning;

using CrystalHarness.Approvals;

namespace CrystalHarness.Configuration;

/// <summary>
/// Loaded host settings: selected provider and model plus the provider catalog.
/// </summary>
public sealed record HarnessSettings
{
    public const double DefaultCompactionThreshold = 0.8;

    public HarnessSettings(
        ProviderName provider,
        string model,
        ApprovalMode approval,
        double compactionThreshold,
        ProviderCatalog catalog,
        ThinkingSelection? thinkingEffort = null)
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
    }

    public ProviderName Provider { get; }

    public string Model { get; }

    public ApprovalMode Approval { get; }

    public double CompactionThreshold { get; }

    public ProviderCatalog Catalog { get; }

    public ThinkingSelection ThinkingEffort { get; }

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

        return new HarnessSettings(
            nextProvider,
            nextModel,
            Approval,
            CompactionThreshold,
            Catalog,
            ThinkingEffort);
    }

    public HarnessSettings WithApproval(ApprovalMode approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return new HarnessSettings(
            Provider,
            Model,
            approval,
            CompactionThreshold,
            Catalog,
            ThinkingEffort);
    }

    public HarnessSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new HarnessSettings(
            Provider,
            Model,
            Approval,
            CompactionThreshold,
            Catalog,
            thinkingEffort);
    }

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
