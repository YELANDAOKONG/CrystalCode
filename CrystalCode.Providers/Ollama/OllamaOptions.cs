using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Ollama;

public sealed record OllamaOptions
{
    public OllamaOptions(
        string model,
        Uri? baseUri = null,
        string? apiKey = null,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        string? vendorName = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(temperature));
        }

        if (topP is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(topP));
        }

        if (maxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens));
        }

        if (requestTimeout is { Ticks: <= 0 })
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        Model = model;
        BaseUri = CompatibleWire.NormalizeBaseUri(baseUri ?? new Uri("http://localhost:11434/"));
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim();
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        VendorName = string.IsNullOrWhiteSpace(vendorName) ? "Ollama" : vendorName.Trim();
        RequestTimeout = requestTimeout ?? TimeSpan.FromMinutes(10);
    }

    public string Model { get; }
    public Uri BaseUri { get; }
    public string ApiKey { get; }
    public double? Temperature { get; }
    public double? TopP { get; }
    public int? MaxTokens { get; }
    public string VendorName { get; }
    public TimeSpan RequestTimeout { get; }

    internal ProtocolOptions ToProtocolOptions() =>
        new(ApiKey, Model, BaseUri, Temperature, TopP, MaxTokens, RequestTimeout, VendorName);

    public override string ToString() => nameof(OllamaOptions);
}
