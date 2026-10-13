namespace CrystalCode.Plugins.Clients;

/// <summary>
/// Endpoint facts a disk plugin needs to build a chat client.
/// The API key is present for the call and must not be logged.
/// </summary>
public sealed record PluginClientRequest
{
    public PluginClientRequest(
        string protocol,
        string provider,
        string model,
        Uri baseUri,
        string apiKey,
        string? organization,
        string? project,
        double? temperature,
        double? topP,
        int? maxTokens,
        bool replayReasoningContent,
        string tokenLimit,
        bool imageInput,
        bool requiresApiKey,
        bool audioInput = false,
        bool videoInput = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenLimit);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));
        }

        Protocol = protocol.Trim();
        Provider = provider.Trim();
        Model = model.Trim();
        BaseUri = baseUri;
        ApiKey = apiKey ?? string.Empty;
        Organization = organization;
        Project = project;
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        ReplayReasoningContent = replayReasoningContent;
        TokenLimit = tokenLimit.Trim();
        ImageInput = imageInput;
        AudioInput = audioInput;
        VideoInput = videoInput;
        RequiresApiKey = requiresApiKey;
    }

    public string Protocol { get; }

    public string Provider { get; }

    public string Model { get; }

    public Uri BaseUri { get; }

    public string ApiKey { get; }

    public string? Organization { get; }

    public string? Project { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

    public int? MaxTokens { get; }

    public bool ReplayReasoningContent { get; }

    public string TokenLimit { get; }

    public bool ImageInput { get; }

    /// <summary>
    /// Whether the model entry declares audio input. Built-in adapters do
    /// not accept or send audio.
    /// </summary>
    public bool AudioInput { get; }

    /// <summary>
    /// Whether the model entry declares video input. Built-in adapters do
    /// not accept or send video.
    /// </summary>
    public bool VideoInput { get; }

    public bool RequiresApiKey { get; }

    public override string ToString() => nameof(PluginClientRequest);
}
