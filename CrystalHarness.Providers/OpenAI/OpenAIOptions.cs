using CrystalHarness.Providers.Compatible;

namespace CrystalHarness.Providers.OpenAI;

/// <summary>
/// Configures one OpenAI Chat Completions adapter instance.
/// </summary>
public sealed record OpenAIOptions
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Initializes OpenAI adapter options.
    /// </summary>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="model">The configured OpenAI model identifier.</param>
    /// <param name="baseUri">
    /// The API base URI. Defaults to <c>https://api.openai.com/v1/</c>.
    /// Compatible gateways may supply another absolute URI.
    /// </param>
    /// <param name="organization">Optional OpenAI organization identifier.</param>
    /// <param name="project">Optional OpenAI project identifier.</param>
    /// <param name="temperature">Optional sampling temperature in the range 0 to 2.</param>
    /// <param name="topP">Optional nucleus sampling probability in the range 0 to 1.</param>
    /// <param name="maxTokens">
    /// Optional positive maximum output-token count. Sent as
    /// <c>max_completion_tokens</c>.
    /// </param>
    /// <param name="replayReasoningContent">
    /// When true, assistant <c>reasoning_content</c> is written back on later
    /// turns. Official OpenAI Chat Completions rejects that field; enable it
    /// only for compatible endpoints that accept it.
    /// </param>
    /// <param name="useMaxCompletionTokens">
    /// When true, the output cap is sent as <c>max_completion_tokens</c>.
    /// Compatible gateways that still expect <c>max_tokens</c> set this false.
    /// </param>
    /// <param name="vendorName">
    /// Name used in adapter errors. Defaults to <c>OpenAI</c>.
    /// </param>
    /// <param name="requestTimeout">
    /// Timeout applied when this adapter creates its own <see cref="HttpClient"/>.
    /// </param>
    public OpenAIOptions(
        string apiKey,
        string model,
        Uri? baseUri = null,
        string? organization = null,
        string? project = null,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        bool replayReasoningContent = false,
        bool useMaxCompletionTokens = true,
        string? vendorName = null,
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
        BaseUri = CompatibleWire.NormalizeBaseUri(
            baseUri ?? new Uri("https://api.openai.com/v1/"));
        Organization = organization;
        Project = project;
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        ReplayReasoningContent = replayReasoningContent;
        UseMaxCompletionTokens = useMaxCompletionTokens;
        VendorName = string.IsNullOrWhiteSpace(vendorName) ? "OpenAI" : vendorName.Trim();
        RequestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    /// <inheritdoc />
    public override string ToString() => nameof(OpenAIOptions);

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
    /// Gets the optional organization identifier.
    /// </summary>
    public string? Organization { get; }

    /// <summary>
    /// Gets the optional project identifier.
    /// </summary>
    public string? Project { get; }

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
    /// Gets whether assistant reasoning content is written back on later turns.
    /// </summary>
    public bool ReplayReasoningContent { get; }

    /// <summary>
    /// Gets whether the output cap uses <c>max_completion_tokens</c>.
    /// </summary>
    public bool UseMaxCompletionTokens { get; }

    /// <summary>
    /// Gets the name used in adapter errors.
    /// </summary>
    public string VendorName { get; }

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
            Organization,
            Project);
}
