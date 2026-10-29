namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional vision model used to describe images for a session model that
/// cannot see them. Omitted provider and model leave the tool unregistered.
/// Thinking capability stays on the model entry; this stores the gear and the
/// model's own service tier and speed. An unset slot follows the per-model
/// tier or speed selection.
/// </summary>
public sealed record ImageModelSettings
{
    public static ImageModelSettings None { get; } = new(null, null);

    public ImageModelSettings(
        string? provider,
        string? model,
        ThinkingSelection? thinkingEffort = null,
        string? serviceTier = null,
        string? speed = null)
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
    }

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

    public bool IsConfigured => Provider is not null && Model is not null;

    public ImageModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Provider, Model, thinkingEffort, ServiceTier, Speed);
    }

    public ImageModelSettings WithServiceTier(string? serviceTier) =>
        new(Provider, Model, ThinkingEffort, serviceTier, Speed);

    public ImageModelSettings WithSpeed(string? speed) =>
        new(Provider, Model, ThinkingEffort, ServiceTier, speed);

    public override string ToString() => nameof(ImageModelSettings);
}
