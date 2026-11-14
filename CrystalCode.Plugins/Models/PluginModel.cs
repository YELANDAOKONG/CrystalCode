namespace CrystalCode.Plugins.Models;

/// <summary>
/// One model the session can name. It carries the provider's base URI, with
/// any user information removed. It carries no credential, organization, or
/// project.
/// </summary>
public sealed record PluginModel
{
    /// <summary>
    /// A model the host has not published. Strings are empty and the
    /// context window is zero.
    /// </summary>
    public static PluginModel Empty { get; } = new(unavailable: true);

    public PluginModel(
        string provider,
        string protocol,
        string name,
        int contextWindow,
        int? maxTokens,
        string thinking,
        bool imageInput,
        bool audioInput = false,
        bool videoInput = false,
        bool fileInput = false,
        string? serviceTier = null,
        string? speed = null,
        Uri? baseUri = null)
        : this(
            provider,
            protocol,
            name,
            contextWindow,
            maxTokens,
            thinking,
            imageInput,
            audioInput,
            videoInput,
            fileInput,
            serviceTier,
            speed,
            baseUri,
            unavailable: false)
    {
    }

    private PluginModel(bool unavailable)
        : this(
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            null,
            string.Empty,
            imageInput: false,
            audioInput: false,
            videoInput: false,
            fileInput: false,
            serviceTier: null,
            speed: null,
            baseUri: null,
            unavailable: unavailable)
    {
    }

    private PluginModel(
        string provider,
        string protocol,
        string name,
        int contextWindow,
        int? maxTokens,
        string thinking,
        bool imageInput,
        bool audioInput,
        bool videoInput,
        bool fileInput,
        string? serviceTier,
        string? speed,
        Uri? baseUri,
        bool unavailable)
    {
        if (!unavailable)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(thinking);
            if (contextWindow <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contextWindow),
                    contextWindow,
                    "Context window must be positive.");
            }

            if (maxTokens is <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxTokens),
                    maxTokens,
                    "Maximum token count must be positive.");
            }

            provider = provider.Trim();
            protocol = protocol.Trim();
            name = name.Trim();
            thinking = thinking.Trim();
        }

        Provider = provider;
        Protocol = protocol;
        Name = name;
        ContextWindow = contextWindow;
        MaxTokens = maxTokens;
        ImageInput = imageInput;
        AudioInput = audioInput;
        VideoInput = videoInput;
        FileInput = fileInput;
        Thinking = thinking;
        ServiceTier = string.IsNullOrWhiteSpace(serviceTier) ? null : serviceTier.Trim();
        Speed = string.IsNullOrWhiteSpace(speed) ? null : speed.Trim();
        BaseUri = WithoutUserInfo(baseUri);
    }

    private static Uri? WithoutUserInfo(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.UserInfo.Length == 0)
        {
            return uri;
        }

        return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri;
    }

    public string Provider { get; }

    public string Protocol { get; }

    public string Name { get; }

    public int ContextWindow { get; }

    public int? MaxTokens { get; }

    public bool ImageInput { get; }

    /// <summary>
    /// Whether the model entry declares audio input. The host does not
    /// accept or send audio.
    /// </summary>
    public bool AudioInput { get; }

    /// <summary>
    /// Whether the model entry declares video input. The host does not
    /// accept or send video.
    /// </summary>
    public bool VideoInput { get; }

    /// <summary>Whether the model entry declares file input.</summary>
    public bool FileInput { get; }

    /// <summary>
    /// Host thinking gear. <c>default</c> and <c>off</c> are host sentinels.
    /// Other values are effort names such as <c>low</c> or <c>high</c>.
    /// </summary>
    public string Thinking { get; }

    /// <summary>
    /// Selected service-tier token, or null when the slot is unset.
    /// The host does not interpret the token.
    /// </summary>
    public string? ServiceTier { get; }

    /// <summary>
    /// Selected speed token, or null when the slot is unset.
    /// The host does not interpret the token.
    /// </summary>
    public string? Speed { get; }

    /// <summary>
    /// The provider's API endpoint with user information removed, or null when
    /// the host did not publish one. A credential in the query string is not
    /// removed, so keep keys out of the configured URI.
    /// </summary>
    public Uri? BaseUri { get; }

    public override string ToString() =>
        Name.Length == 0 ? nameof(PluginModel) : Provider + " / " + Name;
}
