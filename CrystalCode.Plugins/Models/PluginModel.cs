namespace CrystalCode.Plugins.Models;

/// <summary>
/// One model the session can name. It carries no endpoint, credential,
/// organization, or project.
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
        double? temperature,
        double? topP,
        bool imageInput,
        string thinking,
        bool audioInput = false,
        bool videoInput = false,
        string? serviceTier = null,
        string? speed = null)
        : this(
            provider,
            protocol,
            name,
            contextWindow,
            maxTokens,
            temperature,
            topP,
            imageInput,
            thinking,
            unavailable: false,
            audioInput,
            videoInput,
            serviceTier,
            speed)
    {
    }

    private PluginModel(bool unavailable)
        : this(
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            null,
            null,
            null,
            false,
            string.Empty,
            unavailable,
            audioInput: false,
            videoInput: false,
            serviceTier: null,
            speed: null)
    {
    }

    private PluginModel(
        string provider,
        string protocol,
        string name,
        int contextWindow,
        int? maxTokens,
        double? temperature,
        double? topP,
        bool imageInput,
        string thinking,
        bool unavailable,
        bool audioInput,
        bool videoInput,
        string? serviceTier,
        string? speed)
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

            if (temperature is < 0 or > 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(temperature),
                    temperature,
                    "Temperature must be between 0 and 2.");
            }

            if (topP is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(topP),
                    topP,
                    "Top-P must be between 0 and 1.");
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
        Temperature = temperature;
        TopP = topP;
        ImageInput = imageInput;
        AudioInput = audioInput;
        VideoInput = videoInput;
        Thinking = thinking;
        ServiceTier = string.IsNullOrWhiteSpace(serviceTier) ? null : serviceTier.Trim();
        Speed = string.IsNullOrWhiteSpace(speed) ? null : speed.Trim();
    }

    public string Provider { get; }

    public string Protocol { get; }

    public string Name { get; }

    public int ContextWindow { get; }

    public int? MaxTokens { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

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

    public override string ToString() =>
        Name.Length == 0 ? nameof(PluginModel) : Provider + " / " + Name;
}
