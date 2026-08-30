namespace CrystalHarness.Configuration;

/// <summary>
/// Per-model limits and sampling. Context size is required; other fields
/// are optional request overrides.
/// </summary>
public sealed record ModelSettings
{
    public ModelSettings(
        int contextWindow,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null)
    {
        if (contextWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contextWindow),
                contextWindow,
                "Context window must be positive.");
        }

        if (temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                temperature,
                "Temperature must be between 0 and 2.");
        }

        if (topP is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topP),
                topP,
                "Top-P must be between 0 and 1.");
        }

        if (maxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTokens),
                maxTokens,
                "Maximum token count must be positive.");
        }

        ContextWindow = contextWindow;
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
    }

    public int ContextWindow { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

    public int? MaxTokens { get; }

    public override string ToString() => nameof(ModelSettings);
}
