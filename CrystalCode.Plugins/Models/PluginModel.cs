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
        string thinking)
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
            null,
            null,
            false,
            string.Empty,
            unavailable)
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
        Thinking = thinking;
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
    /// Host thinking gear. <c>default</c> and <c>off</c> are host sentinels.
    /// Other values are effort names such as <c>low</c> or <c>high</c>.
    /// </summary>
    public string Thinking { get; }

    public override string ToString() =>
        Name.Length == 0 ? nameof(PluginModel) : Provider + " / " + Name;
}
