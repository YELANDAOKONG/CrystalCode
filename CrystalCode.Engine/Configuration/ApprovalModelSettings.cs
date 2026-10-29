namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional reviewer model, its thinking gear, and its own service tier,
/// speed, and context budget. While disabled, a stored provider, model, gear,
/// and slots are kept and the session model reviews instead. An unset tier or
/// speed is not sent, and an unset context budget is the model's default; the
/// session's per-model selection does not apply.
/// </summary>
public sealed record ApprovalModelSettings
{
    public static ApprovalModelSettings Off { get; } = new(false, null, null);

    public ApprovalModelSettings(
        bool enabled,
        string? provider,
        string? model,
        ThinkingSelection? thinkingEffort = null,
        string? serviceTier = null,
        string? speed = null,
        int? contextWindow = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (enabled && (Provider is null || Model is null))
        {
            throw new ArgumentException("Set an approval model before turning it on.");
        }

        Enabled = enabled;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        ServiceTier = serviceTier is null ? null : new CatalogToken(serviceTier).Value;
        Speed = speed is null ? null : new CatalogToken(speed).Value;
        if (contextWindow is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextWindow), contextWindow, "Context window must be positive.");
        }

        ContextWindow = contextWindow;
    }

    public bool Enabled { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public ThinkingSelection ThinkingEffort { get; }

    /// <summary>
    /// Service-tier token for this model only, or null to send none.
    /// </summary>
    public string? ServiceTier { get; }

    /// <summary>
    /// Speed token for this model only, or null to send none.
    /// </summary>
    public string? Speed { get; }

    /// <summary>
    /// Context budget for this model only, or null for the model's default budget.
    /// </summary>
    public int? ContextWindow { get; }

    public bool HasSelection => Provider is not null && Model is not null;

    public ApprovalModelSettings EnabledCopy() =>
        new(true, Provider, Model, ThinkingEffort, ServiceTier, Speed, ContextWindow);

    public ApprovalModelSettings DisabledCopy() =>
        new(false, Provider, Model, ThinkingEffort, ServiceTier, Speed, ContextWindow);

    public ApprovalModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Enabled, Provider, Model, thinkingEffort, ServiceTier, Speed, ContextWindow);
    }

    public ApprovalModelSettings WithServiceTier(string? serviceTier) =>
        new(Enabled, Provider, Model, ThinkingEffort, serviceTier, Speed, ContextWindow);

    public ApprovalModelSettings WithSpeed(string? speed) =>
        new(Enabled, Provider, Model, ThinkingEffort, ServiceTier, speed, ContextWindow);

    public ApprovalModelSettings WithContextWindow(int? contextWindow) =>
        new(Enabled, Provider, Model, ThinkingEffort, ServiceTier, Speed, contextWindow);

    /// <summary>
    /// Keeps the switch and the selection, and takes the gear and slots from
    /// <paramref name="source"/>. A launch gear or slot stays process-only.
    /// </summary>
    public ApprovalModelSettings WithTuningFrom(ApprovalModelSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(Enabled, Provider, Model, source.ThinkingEffort, source.ServiceTier, source.Speed, source.ContextWindow);
    }

    public string Describe() => Describe("Approval model");

    public string Describe(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var state = Enabled ? "On" : "Off";
        if (!HasSelection)
        {
            return label + "  " + state;
        }

        var description = label + "  " + state + "  " + Provider + "  " + Model;
        if (ThinkingEffort != ThinkingSelection.Default)
        {
            description += "  ·  Think " + ThinkingLabel.For(ThinkingEffort);
        }

        if (ServiceTier is not null)
        {
            description += "  ·  Tier " + DisplayCase.Token(ServiceTier);
        }

        if (Speed is not null)
        {
            description += "  ·  Speed " + DisplayCase.Token(Speed);
        }

        if (ContextWindow is int window)
        {
            description += "  ·  Context " + ContextWindowSelection.Format(window);
        }

        return description;
    }
}
