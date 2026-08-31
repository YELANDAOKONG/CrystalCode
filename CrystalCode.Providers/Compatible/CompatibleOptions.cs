namespace CrystalCode.Providers.Compatible;

internal sealed record CompatibleOptions
{
    public CompatibleOptions(
        string apiKey,
        string model,
        Uri baseUri,
        double? temperature,
        double? topP,
        int? maxTokens,
        TimeSpan requestTimeout,
        string? organization,
        string? project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(baseUri);

        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));
        }

        ApiKey = apiKey;
        Model = model;
        BaseUri = baseUri;
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        RequestTimeout = requestTimeout;
        Organization = organization;
        Project = project;
    }

    public string ApiKey { get; }

    public string Model { get; }

    public Uri BaseUri { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

    public int? MaxTokens { get; }

    public TimeSpan RequestTimeout { get; }

    public string? Organization { get; }

    public string? Project { get; }
}
