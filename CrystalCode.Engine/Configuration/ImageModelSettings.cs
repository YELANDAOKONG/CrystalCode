namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional vision model used to describe images for a session model that
/// cannot see them. Omitted provider and model leave the tool unregistered.
/// Thinking capability stays on the model entry; this stores the gear and the
/// model's own service tier, speed, and context budget. An unset slot sends no tier or speed;
/// the session's per-model selection does not apply.
/// </summary>
public sealed record ImageModelSettings
{
    public static ImageModelSettings None { get; } = new(null, null);

    public ImageModelSettings(
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
            throw new ArgumentException("Set both the image model provider and model.");
        }

        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
        ServiceTier = serviceTier is null ? null : new CatalogToken(serviceTier).Value;
        Speed = speed is null ? null : new CatalogToken(speed).Value;
        if (contextWindow is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextWindow), contextWindow, "Context window must be positive.");
        }

        ContextWindow = contextWindow;
    }

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

    public bool IsConfigured => Provider is not null && Model is not null;

    public ImageModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Provider, Model, thinkingEffort, ServiceTier, Speed, ContextWindow);
    }

    public ImageModelSettings WithServiceTier(string? serviceTier) =>
        new(Provider, Model, ThinkingEffort, serviceTier, Speed, ContextWindow);

    public ImageModelSettings WithSpeed(string? speed) =>
        new(Provider, Model, ThinkingEffort, ServiceTier, speed, ContextWindow);

    public ImageModelSettings WithContextWindow(int? contextWindow) =>
        new(Provider, Model, ThinkingEffort, ServiceTier, Speed, contextWindow);

    public override string ToString() => nameof(ImageModelSettings);
}
