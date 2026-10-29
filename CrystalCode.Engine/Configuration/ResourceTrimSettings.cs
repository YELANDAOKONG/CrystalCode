namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Limits how many session resources each model request carries. Only images
/// exist today; audio and video follow the same limits once supported. A
/// resource outside the limits stays in the session and appears to the model
/// as its marker text only, such as <c>[Image #N]</c>.
/// </summary>
public sealed record ResourceTrimSettings
{
    public const bool DefaultEnabled = true;

    public const int DefaultKeepRecent = 16;

    public const int DefaultSlack = 8;

    public const long DefaultMaxBytes = 128L * 1024 * 1024;

    public const bool DefaultIncludeUserResources = false;

    public static ResourceTrimSettings Default { get; } = new(
        DefaultEnabled,
        DefaultKeepRecent,
        DefaultSlack,
        DefaultMaxBytes,
        DefaultIncludeUserResources);

    public ResourceTrimSettings(
        bool enabled,
        int keepRecent,
        int slack,
        long maxBytes,
        bool includeUserResources)
    {
        if (keepRecent < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keepRecent),
                keepRecent,
                "Resource trim keep recent must be at least 1.");
        }

        if (slack < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slack),
                slack,
                "Resource trim slack cannot be negative.");
        }

        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes),
                maxBytes,
                "Resource trim max bytes cannot be negative.");
        }

        Enabled = enabled;
        KeepRecent = keepRecent;
        Slack = slack;
        MaxBytes = maxBytes;
        IncludeUserResources = includeUserResources;
    }

    public bool Enabled { get; }

    /// <summary>Resources kept in a request once trimming triggers.</summary>
    public int KeepRecent { get; }

    /// <summary>
    /// Resources allowed beyond <see cref="KeepRecent"/> before trimming triggers,
    /// so the request prefix changes only now and then.
    /// </summary>
    public int Slack { get; }

    /// <summary>Byte budget for the images in one request. Zero means no budget.</summary>
    public long MaxBytes { get; }

    /// <summary>Whether resources the operator attached are trimmed like tool resources.</summary>
    public bool IncludeUserResources { get; }

    public bool IsDefault =>
        Enabled == DefaultEnabled
        && KeepRecent == DefaultKeepRecent
        && Slack == DefaultSlack
        && MaxBytes == DefaultMaxBytes
        && IncludeUserResources == DefaultIncludeUserResources;

    public ResourceTrimSettings WithEnabled(bool enabled) =>
        new(enabled, KeepRecent, Slack, MaxBytes, IncludeUserResources);
}
