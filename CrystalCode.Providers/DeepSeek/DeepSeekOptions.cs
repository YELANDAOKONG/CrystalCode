using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.DeepSeek;

/// <summary>
/// Configures one DeepSeek chat adapter instance.
/// </summary>
public sealed record DeepSeekOptions
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Initializes DeepSeek adapter options.
    /// </summary>
    /// <param name="apiKey">The DeepSeek API key.</param>
    /// <param name="model">The configured DeepSeek model identifier.</param>
    /// <param name="baseUri">
    /// The API base URI. Defaults to <c>https://api.deepseek.com/</c>.
    /// </param>
    /// <param name="temperature">Optional sampling temperature in the range 0 to 2.</param>
    /// <param name="topP">Optional nucleus sampling probability in the range 0 to 1.</param>
    /// <param name="maxTokens">Optional positive maximum output-token count.</param>
    /// <param name="requestTimeout">
    /// Timeout applied when this adapter creates its own <see cref="HttpClient"/>.
    /// </param>
    public DeepSeekOptions(
        string apiKey,
        string model,
        Uri? baseUri = null,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (baseUri is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));
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

        if (maxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTokens),
                maxTokens,
                "Maximum token count must be positive.");
        }

        if (requestTimeout is { Ticks: <= 0 })
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                requestTimeout,
                "Request timeout must be positive.");
        }

        ApiKey = apiKey;
        Model = model;
        BaseUri = CompatibleWire.NormalizeBaseUri(baseUri ?? new Uri("https://api.deepseek.com/"));
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        RequestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    /// <inheritdoc />
    public override string ToString() => nameof(DeepSeekOptions);

    /// <summary>
    /// Gets the API key.
    /// </summary>
    public string ApiKey { get; }

    /// <summary>
    /// Gets the configured model identifier.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the API base URI.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>
    /// Gets the optional sampling temperature.
    /// </summary>
    public double? Temperature { get; }

    /// <summary>
    /// Gets the optional nucleus sampling probability.
    /// </summary>
    public double? TopP { get; }

    /// <summary>
    /// Gets the optional maximum output-token count.
    /// </summary>
    public int? MaxTokens { get; }

    /// <summary>
    /// Gets the timeout used for an adapter-owned HTTP client.
    /// </summary>
    public TimeSpan RequestTimeout { get; }

    internal CompatibleOptions ToCompatibleOptions() =>
        new(
            ApiKey,
            Model,
            BaseUri,
            Temperature,
            TopP,
            MaxTokens,
            RequestTimeout,
            organization: null,
            project: null);
}
