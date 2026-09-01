namespace CrystalCode.Providers.Protocol;

internal sealed record ProtocolOptions(
    string ApiKey,
    string Model,
    Uri BaseUri,
    double? Temperature,
    double? TopP,
    int? MaxTokens,
    TimeSpan RequestTimeout,
    string VendorName);
