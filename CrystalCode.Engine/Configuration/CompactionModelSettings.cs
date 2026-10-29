namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional summarizer model, its thinking gear, and its own service tier and
/// speed. While disabled, a stored provider, model, gear, and slots are kept
/// and the session model summarizes instead. An unset slot follows the
/// per-model tier or speed selection.
/// </summary>
public sealed record CompactionModelSettings
{
    public static CompactionModelSettings Off { get; } = new(false, null, null);

    public CompactionModelSettings(
        bool enabled,
        string? provider,
        string? model,
        ThinkingSelection? thinkingEffort = null,
        string? serviceTier = null,
        string? speed = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (enabled && (Provider is null || Model is null))
        {
            throw new ArgumentException("Set a compaction model before turning it on.");
        }

        Enabled = enabled;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        ServiceTier = serviceTier is null ? null : new CatalogToken(serviceTier).Value;
        Speed = speed is null ? null : new CatalogToken(speed).Value;
    }

    public bool Enabled { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public ThinkingSelection ThinkingEffort { get; }

    /// <summary>
    /// Service-tier token for this model only, or null to follow the per-model selection.
    /// </summary>
    public string? ServiceTier { get; }

    /// <summary>
    /// Speed token for this model only, or null to follow the per-model selection.
    /// </summary>
    public string? Speed { get; }

    public bool HasSelection => Provider is not null && Model is not null;

    public CompactionModelSettings EnabledCopy() =>
        new(true, Provider, Model, ThinkingEffort, ServiceTier, Speed);

    public CompactionModelSettings DisabledCopy() =>
        new(false, Provider, Model, ThinkingEffort, ServiceTier, Speed);

    public CompactionModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Enabled, Provider, Model, thinkingEffort, ServiceTier, Speed);
    }

    public CompactionModelSettings WithServiceTier(string? serviceTier) =>
        new(Enabled, Provider, Model, ThinkingEffort, serviceTier, Speed);

    public CompactionModelSettings WithSpeed(string? speed) =>
        new(Enabled, Provider, Model, ThinkingEffort, ServiceTier, speed);

    /// <summary>
    /// Keeps the switch and the selection, and takes the gear and slots from
    /// <paramref name="source"/>. A launch gear or slot stays process-only.
    /// </summary>
    public CompactionModelSettings WithTuningFrom(CompactionModelSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(Enabled, Provider, Model, source.ThinkingEffort, source.ServiceTier, source.Speed);
    }

    public string Describe()
    {
        var state = Enabled ? "On" : "Off";
        if (!HasSelection)
        {
            return "Compaction model  " + state;
        }

        var description = "Compaction model  " + state + "  " + Provider + "  " + Model;
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

        return description;
    }
}
