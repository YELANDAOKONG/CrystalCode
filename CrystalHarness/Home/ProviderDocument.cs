namespace CrystalHarness.Home;

internal sealed class ProviderDocument
{
    public string? Protocol { get; set; }

    public string? BaseUri { get; set; }

    public string? Organization { get; set; }

    public string? Project { get; set; }

    public bool? ReplayReasoningContent { get; set; }

    public string? TokenLimit { get; set; }

    public string? ApiKeyEnvironment { get; set; }

    public string? ApiKey { get; set; }

    public Dictionary<string, ModelDocument>? Models { get; set; }
}
