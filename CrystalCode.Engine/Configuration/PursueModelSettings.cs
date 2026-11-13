namespace CrystalCode.Engine.Configuration;

/// <summary>
/// The model that judges whether a pursued objective is met. With no
/// provider and model it uses the session model and that model's current
/// tier, speed, and context selection. With its own model it is configured on
/// its own, like the topic model: an unset tier or speed is not sent and an
/// unset context is the model's first declared budget. The thinking gear is
/// always this setting's.
/// </summary>
public sealed record PursueModelSettings
{
    public PursueModelSettings(
        string? provider = null,
        string? model = null,
        ThinkingSelection? thinkingEffort = null,
        string? serviceTier = null,
        string? speed = null,
        int? contextWindow = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if ((Provider is null) != (Model is null))
        {
            throw new ArgumentException("Set both the pursue model provider and model, or neither to use the session model.");
        }

        if (contextWindow is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextWindow), contextWindow, "Context window must be positive.");
        }

        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        ServiceTier = serviceTier is null ? null : new CatalogToken(serviceTier).Value;
        Speed = speed is null ? null : new CatalogToken(speed).Value;
        ContextWindow = contextWindow;
    }

    public static PursueModelSettings Default { get; } = new();

    /// <summary>Provider of the pursue model, or null to use the session model.</summary>
    public string? Provider { get; }

    public string? Model { get; }

    public ThinkingSelection ThinkingEffort { get; }

    /// <summary>Service-tier token for its own model, or null to send none. Ignored for the session model.</summary>
    public string? ServiceTier { get; }

    /// <summary>Speed token for its own model, or null to send none. Ignored for the session model.</summary>
    public string? Speed { get; }

    /// <summary>Context budget for its own model, or null for its first declared budget. Ignored for the session model.</summary>
    public int? ContextWindow { get; }

    public bool UsesSessionModel => Provider is null;

    public bool IsDefault =>
        UsesSessionModel
        && ThinkingEffort == ThinkingSelection.Default
        && ServiceTier is null
        && Speed is null
        && ContextWindow is null;

    /// <summary>
    /// Selects an own model, or the session model with nulls. A tier, speed,
    /// or context budget belongs to one model, so another selection clears them.
    /// </summary>
    public PursueModelSettings WithModel(string? provider, string? model)
    {
        var same = string.Equals(provider, Provider, StringComparison.Ordinal)
            && string.Equals(model, Model, StringComparison.Ordinal);
        return same ? this : new(provider, model, ThinkingEffort);
    }

    public PursueModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Provider, Model, thinkingEffort, ServiceTier, Speed, ContextWindow);
    }

    public PursueModelSettings WithServiceTier(string? serviceTier) =>
        new(Provider, Model, ThinkingEffort, serviceTier, Speed, ContextWindow);

    public PursueModelSettings WithSpeed(string? speed) =>
        new(Provider, Model, ThinkingEffort, ServiceTier, speed, ContextWindow);

    public PursueModelSettings WithContextWindow(int? contextWindow) =>
        new(Provider, Model, ThinkingEffort, ServiceTier, Speed, contextWindow);

    public string Describe()
    {
        var description = "Pursue model  " + (UsesSessionModel ? "session model" : Provider + "  " + Model);
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

    public override string ToString() => nameof(PursueModelSettings);
}
