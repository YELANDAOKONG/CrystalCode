using System.Collections.ObjectModel;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// One named provider endpoint and its model table.
/// </summary>
public sealed record ProviderDefinition
{
    public ProviderDefinition(
        ProviderName name,
        ProviderProtocol protocol,
        Uri baseUri,
        IReadOnlyDictionary<string, ModelSettings> models,
        string? organization = null,
        string? project = null,
        bool replayReasoningContent = false,
        TokenLimitStyle? tokenLimit = null,
        string? apiKeyEnvironment = null,
        string? apiKey = null,
        bool? requiresApiKey = null,
        ThinkingDisplay? thinkingDisplay = null,
        ResourceTrimOverride? resourceTrim = null,
        bool requireAssistantContent = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(models);

        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));
        }

        if (thinkingDisplay is not null
            && thinkingDisplay != ThinkingDisplay.Summarized
            && thinkingDisplay != ThinkingDisplay.Omitted)
        {
            throw new ArgumentException(
                "Thinking display must be summarized or omitted.",
                nameof(thinkingDisplay));
        }

        if (thinkingDisplay is not null && protocol != ProviderProtocol.Anthropic)
        {
            throw new ArgumentException(
                "Thinking display is only valid for the anthropic protocol.",
                nameof(thinkingDisplay));
        }

        Name = name;
        Protocol = protocol;
        BaseUri = baseUri;
        Models = new ReadOnlyDictionary<string, ModelSettings>(
            new Dictionary<string, ModelSettings>(models, StringComparer.Ordinal));
        Organization = organization;
        Project = project;
        ReplayReasoningContent = replayReasoningContent
            || protocol == ProviderProtocol.DeepSeek;
        RequireAssistantContent = requireAssistantContent;
        TokenLimit = tokenLimit ?? TokenLimitStyle.ForProtocol(protocol);
        ApiKeyEnvironment = apiKeyEnvironment;
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        RequiresApiKey = requiresApiKey ?? protocol != ProviderProtocol.Ollama;
        ResourceTrim = resourceTrim;
        ThinkingDisplay = thinkingDisplay == ThinkingDisplay.Omitted
            ? ThinkingDisplay.Omitted
            : null;
    }

    public ProviderName Name { get; }

    public ProviderProtocol Protocol { get; }

    public Uri BaseUri { get; }

    public IReadOnlyDictionary<string, ModelSettings> Models { get; }

    public string? Organization { get; }

    public string? Project { get; }

    public bool ReplayReasoningContent { get; }

    /// <summary>
    /// Gets whether an assistant message with no text is sent with an empty
    /// string content. Only the <c>openai</c> protocol uses it.
    /// </summary>
    public bool RequireAssistantContent { get; }

    public TokenLimitStyle TokenLimit { get; }

    public string? ApiKeyEnvironment { get; }

    /// <summary>
    /// Gets the configured API key text: a literal value, <c>{env:NAME}</c>,
    /// or <c>{file:path}</c>.
    /// </summary>
    public string? ApiKey { get; }

    public bool RequiresApiKey { get; }

    /// <summary>
    /// Gets the Anthropic thinking-text request. Null uses summarized text
    /// and is not written to a provider file.
    /// </summary>
    public ThinkingDisplay? ThinkingDisplay { get; }

    /// <summary>
    /// Gets the resource trim limits this endpoint sets, or null to use the global ones.
    /// </summary>
    public ResourceTrimOverride? ResourceTrim { get; }

    public bool TryGetModel(string model, out ModelSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return Models.TryGetValue(model, out settings!);
    }

    public override string ToString() => Name.Value;
}
