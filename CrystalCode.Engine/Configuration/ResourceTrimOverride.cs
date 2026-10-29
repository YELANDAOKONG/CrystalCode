namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Resource trim limits a provider definition sets for its own endpoint. An
/// omitted value keeps the global <see cref="ResourceTrimSettings"/> value. The
/// on and off switch stays global.
/// </summary>
public sealed record ResourceTrimOverride
{
    public ResourceTrimOverride(
        int? keepRecent = null,
        int? slack = null,
        long? maxBytes = null,
        bool? includeUserResources = null)
    {
        KeepRecent = keepRecent;
        Slack = slack;
        MaxBytes = maxBytes;
        IncludeUserResources = includeUserResources;
        // Fails on an invalid value now, not at the first request.
        ApplyTo(ResourceTrimSettings.Default);
    }

    public int? KeepRecent { get; }

    public int? Slack { get; }

    public long? MaxBytes { get; }

    public bool? IncludeUserResources { get; }

    public ResourceTrimSettings ApplyTo(ResourceTrimSettings global)
    {
        ArgumentNullException.ThrowIfNull(global);
        return new ResourceTrimSettings(
            global.Enabled,
            KeepRecent ?? global.KeepRecent,
            Slack ?? global.Slack,
            MaxBytes ?? global.MaxBytes,
            IncludeUserResources ?? global.IncludeUserResources);
    }
}
