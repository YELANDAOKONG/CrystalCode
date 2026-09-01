using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Responses;

public sealed record ResponsesOptions
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(10);

    public ResponsesOptions(
        string apiKey,
        string model,
        Uri baseUri,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        string? vendorName = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));
        }

        if (temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), temperature, "Temperature must be between 0 and 2.");
        }

        if (topP is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(topP), topP, "Top-P must be between 0 and 1.");
        }

        if (maxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens, "Maximum token count must be positive.");
        }

        if (requestTimeout is { Ticks: <= 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), requestTimeout, "Request timeout must be positive.");
        }

        ApiKey = apiKey;
        Model = model;
        BaseUri = CompatibleWire.NormalizeBaseUri(baseUri);
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        VendorName = string.IsNullOrWhiteSpace(vendorName) ? "OpenAI Responses" : vendorName.Trim();
        RequestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    public string ApiKey { get; }
    public string Model { get; }
    public Uri BaseUri { get; }
    public double? Temperature { get; }
    public double? TopP { get; }
    public int? MaxTokens { get; }
    public string VendorName { get; }
    public TimeSpan RequestTimeout { get; }

    internal ProtocolOptions ToProtocolOptions() =>
        new(ApiKey, Model, BaseUri, Temperature, TopP, MaxTokens, RequestTimeout, VendorName);
}
