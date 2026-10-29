namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Limits how many session images each model request carries. Images that fall
/// outside the limits stay in the session and appear to the model as their
/// <c>[Image #N]</c> marker text only.
/// </summary>
public sealed record ImageTrimSettings
{
    public const bool DefaultEnabled = true;

    public const int DefaultKeepRecent = 16;

    public const int DefaultSlack = 8;

    public const long DefaultMaxBytes = 128L * 1024 * 1024;

    public const bool DefaultIncludeUserImages = false;

    public static ImageTrimSettings Default { get; } = new(
        DefaultEnabled,
        DefaultKeepRecent,
        DefaultSlack,
        DefaultMaxBytes,
        DefaultIncludeUserImages);

    public ImageTrimSettings(
        bool enabled,
        int keepRecent,
        int slack,
        long maxBytes,
        bool includeUserImages)
    {
        if (keepRecent < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keepRecent),
                keepRecent,
                "Image trim keep recent must be at least 1.");
        }

        if (slack < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slack),
                slack,
                "Image trim slack cannot be negative.");
        }

        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes),
                maxBytes,
                "Image trim max bytes cannot be negative.");
        }

        Enabled = enabled;
        KeepRecent = keepRecent;
        Slack = slack;
        MaxBytes = maxBytes;
        IncludeUserImages = includeUserImages;
    }

    public bool Enabled { get; }

    /// <summary>Images kept in a request once trimming triggers.</summary>
    public int KeepRecent { get; }

    /// <summary>
    /// Images allowed beyond <see cref="KeepRecent"/> before trimming triggers,
    /// so the request prefix changes only now and then.
    /// </summary>
    public int Slack { get; }

    /// <summary>Byte budget for the images in one request. Zero means no budget.</summary>
    public long MaxBytes { get; }

    /// <summary>Whether images the operator attached are trimmed like tool images.</summary>
    public bool IncludeUserImages { get; }

    public bool IsDefault =>
        Enabled == DefaultEnabled
        && KeepRecent == DefaultKeepRecent
        && Slack == DefaultSlack
        && MaxBytes == DefaultMaxBytes
        && IncludeUserImages == DefaultIncludeUserImages;

    public ImageTrimSettings WithEnabled(bool enabled) =>
        new(enabled, KeepRecent, Slack, MaxBytes, IncludeUserImages);
}
