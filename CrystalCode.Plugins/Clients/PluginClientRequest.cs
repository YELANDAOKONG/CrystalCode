using System.Text.Json;

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
        bool requiresApiKey,
        string? organization,
        string? project,
        int? maxTokens,
        string tokenLimit,
        bool replayReasoningContent,
        bool imageInput,
        bool audioInput = false,
        bool videoInput = false,
        bool fileInput = false,
        string? serviceTier = null,
        string? speed = null,
        JsonElement? modelOptions = null)
        : this(protocol, provider, model, baseUri, apiKey, requiresApiKey, organization, project, maxTokens, tokenLimit, replayReasoningContent, imageInput, audioInput, videoInput, fileInput, serviceTier, speed, modelOptions, null)
    {
    }

    public PluginClientRequest(
        string protocol,
        string provider,
        string model,
        Uri baseUri,
        string apiKey,
        bool requiresApiKey,
        string? organization,
        string? project,
        int? maxTokens,
        string tokenLimit,
        bool replayReasoningContent,
        bool imageInput,
        bool audioInput,
        bool videoInput,
        bool fileInput,
        string? serviceTier,
        string? speed,
        JsonElement? modelOptions,
        IReadOnlyDictionary<string, string>? httpHeaders)
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
        ModelOptions = modelOptions?.Clone();
        HttpHeaders = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(httpHeaders ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
        MaxTokens = maxTokens;
        ReplayReasoningContent = replayReasoningContent;
        TokenLimit = tokenLimit.Trim();
        ImageInput = imageInput;
        AudioInput = audioInput;
        VideoInput = videoInput;
        FileInput = fileInput;
        RequiresApiKey = requiresApiKey;
        ServiceTier = string.IsNullOrWhiteSpace(serviceTier) ? null : serviceTier.Trim();
        Speed = string.IsNullOrWhiteSpace(speed) ? null : speed.Trim();
    }

    public IReadOnlyDictionary<string, string> HttpHeaders { get; }

    public string Protocol { get; }

    public string Provider { get; }

    public string Model { get; }

    public Uri BaseUri { get; }

    public string ApiKey { get; }

    public string? Organization { get; }

    public string? Project { get; }

    /// <summary>Gets the model options for this factory to interpret.</summary>
    public JsonElement? ModelOptions { get; }

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

    /// <summary>Whether the model entry declares file input.</summary>
    public bool FileInput { get; }

    public bool RequiresApiKey { get; }

    /// <summary>
    /// Selected service-tier token, or null when the slot is unset.
    /// A factory that cannot send a non-null slot must throw.
    /// </summary>
    public string? ServiceTier { get; }

    /// <summary>
    /// Selected speed token, or null when the slot is unset.
    /// A factory that cannot send a non-null slot must throw.
    /// </summary>
    public string? Speed { get; }

    public override string ToString() => nameof(PluginClientRequest);
}
