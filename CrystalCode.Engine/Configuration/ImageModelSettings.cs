namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional vision model used to describe images for a session model that
/// cannot see them. Omitted provider and model leave the tool unregistered.
/// Thinking capability stays on the model entry; this stores only the gear.
/// </summary>
public sealed record ImageModelSettings
{
    public static ImageModelSettings None { get; } = new(null, null);

    public ImageModelSettings(
        string? provider,
        string? model,
        ThinkingSelection? thinkingEffort = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if ((Provider is null) != (Model is null))
        {
            throw new ArgumentException("Set both the image model provider and model.");
        }

        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
    }

    public string? Provider { get; }

    public string? Model { get; }

    public ThinkingSelection ThinkingEffort { get; }

    public bool IsConfigured => Provider is not null && Model is not null;

    public ImageModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Provider, Model, thinkingEffort);
    }

    public override string ToString() => nameof(ImageModelSettings);
}
