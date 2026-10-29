namespace CrystalCode.Engine.Configuration;

/// <summary>
/// A resource description model: the image, audio, or video model that
/// describes that input for a session model that cannot read it. It has its
/// own switch, thinking gear, service tier, speed, and context budget. While
/// disabled, a stored provider, model, gear, and slots are kept unused. An
/// unset tier or speed is not sent, and an unset context budget is the
/// model's first declared budget; the session's per-model selection does not
/// apply. Only the image model is connected to a tool.
/// </summary>
public sealed record ResourceModelSettings
{
    public ResourceModelSettings(
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
        if ((Provider is null) != (Model is null))
        {
            throw new ArgumentException("Set both the provider and the model of a resource description model.");
        }

        if (enabled && Provider is null)
        {
            throw new ArgumentException("Set a resource description model before turning it on.");
        }

        if (contextWindow is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextWindow), contextWindow, "Context window must be positive.");
        }

        Enabled = enabled;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        ServiceTier = serviceTier is null ? null : new CatalogToken(serviceTier).Value;
        Speed = speed is null ? null : new CatalogToken(speed).Value;
        ContextWindow = contextWindow;
    }

    public static ResourceModelSettings Off { get; } = new(false, null, null);

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
    /// Context budget for this model only, or null for the model's first declared budget.
    /// </summary>
    public int? ContextWindow { get; }

    public bool HasSelection => Provider is not null && Model is not null;

    public bool IsEmpty =>
        !Enabled
        && !HasSelection
        && ThinkingEffort == ThinkingSelection.Default
        && ServiceTier is null
        && Speed is null
        && ContextWindow is null;

    public ResourceModelSettings EnabledCopy() =>
        new(true, Provider, Model, ThinkingEffort, ServiceTier, Speed, ContextWindow);

    public ResourceModelSettings DisabledCopy() =>
        new(false, Provider, Model, ThinkingEffort, ServiceTier, Speed, ContextWindow);

    public ResourceModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Enabled, Provider, Model, thinkingEffort, ServiceTier, Speed, ContextWindow);
    }

    public ResourceModelSettings WithServiceTier(string? serviceTier) =>
        new(Enabled, Provider, Model, ThinkingEffort, serviceTier, Speed, ContextWindow);

    public ResourceModelSettings WithSpeed(string? speed) =>
        new(Enabled, Provider, Model, ThinkingEffort, ServiceTier, speed, ContextWindow);

    public ResourceModelSettings WithContextWindow(int? contextWindow) =>
        new(Enabled, Provider, Model, ThinkingEffort, ServiceTier, Speed, contextWindow);

    /// <summary>
    /// Keeps the switch and the selection, and takes the gear and slots from
    /// <paramref name="source"/>. A launch gear or slot stays process-only.
    /// </summary>
    public ResourceModelSettings WithTuningFrom(ResourceModelSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(Enabled, Provider, Model, source.ThinkingEffort, source.ServiceTier, source.Speed, source.ContextWindow);
    }

    public string Describe(ResourceKind kind)
    {
        var label = ResourceKinds.Title(kind) + " model";
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

        if (!ResourceKinds.IsConnected(kind))
        {
            description += "  ·  Not connected yet";
        }

        return description;
    }

    public override string ToString() => nameof(ResourceModelSettings);
}
