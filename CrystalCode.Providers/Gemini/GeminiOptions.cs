using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Gemini;

public sealed record GeminiOptions
{
    public GeminiOptions(
        string apiKey,
        string model,
        Uri? baseUri = null,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        string? vendorName = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
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

        ApiKey = apiKey;
        Model = model;
        BaseUri = CompatibleWire.NormalizeBaseUri(
            baseUri ?? new Uri("https://generativelanguage.googleapis.com/v1beta/"));
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        VendorName = string.IsNullOrWhiteSpace(vendorName) ? "Gemini" : vendorName.Trim();
        RequestTimeout = requestTimeout ?? TimeSpan.FromMinutes(10);
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

    public override string ToString() => nameof(GeminiOptions);
}
