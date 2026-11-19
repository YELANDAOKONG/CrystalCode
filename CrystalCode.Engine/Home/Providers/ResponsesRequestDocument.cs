namespace CrystalCode.Engine.Home.Providers;

internal sealed class ResponsesRequestDocument
{
    public bool? SystemAsInstructions { get; set; }

    public bool? IncludeEncryptedReasoning { get; set; }

    public bool? OmitSampling { get; set; }

    public bool? SystemAsDeveloper { get; set; }

    public bool? SessionAffinity { get; set; }

    public bool? PromptCacheKey { get; set; }
}
