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
        bool requiresApiKey)
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

    public bool RequiresApiKey { get; }

    public override string ToString() => nameof(PluginClientRequest);
}
