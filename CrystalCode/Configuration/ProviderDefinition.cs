namespace CrystalCode.Configuration;

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
        string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(models);

        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));
        }

        Name = name;
        Protocol = protocol;
        BaseUri = baseUri;
        Models = new Dictionary<string, ModelSettings>(models, StringComparer.Ordinal);
        Organization = organization;
        Project = project;
        ReplayReasoningContent = replayReasoningContent
            || protocol == ProviderProtocol.DeepSeek;
        TokenLimit = tokenLimit ?? TokenLimitStyle.ForProtocol(protocol);
        ApiKeyEnvironment = apiKeyEnvironment;
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public ProviderName Name { get; }

    public ProviderProtocol Protocol { get; }

    public Uri BaseUri { get; }

    public IReadOnlyDictionary<string, ModelSettings> Models { get; }

    public string? Organization { get; }

    public string? Project { get; }

    public bool ReplayReasoningContent { get; }

    public TokenLimitStyle TokenLimit { get; }

    public string? ApiKeyEnvironment { get; }

    /// <summary>
    /// Gets the configured API key text: a literal value, <c>{env:NAME}</c>,
    /// or <c>{file:path}</c>.
    /// </summary>
    public string? ApiKey { get; }

    public bool TryGetModel(string model, out ModelSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return Models.TryGetValue(model, out settings!);
    }

    public override string ToString() => Name.Value;
}
