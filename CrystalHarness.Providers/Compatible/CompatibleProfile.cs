namespace CrystalHarness.Providers.Compatible;

internal sealed record CompatibleProfile
{
    public CompatibleProfile(
        string vendorName,
        string chatCompletionsPath,
        string? reasoningStateFormat,
        bool writeReasoningContent,
        bool writeThinkingObject,
        bool supportsMinimalEffort,
        string maximumEffortValue,
        CompatibleTokenLimit tokenLimit,
        CompatibleFaults faults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatCompletionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(maximumEffortValue);
        ArgumentNullException.ThrowIfNull(faults);

        if (writeReasoningContent && string.IsNullOrWhiteSpace(reasoningStateFormat))
        {
            throw new ArgumentException(
                "A reasoning state format is required when reasoning content is written.",
                nameof(reasoningStateFormat));
        }

        VendorName = vendorName;
        ChatCompletionsPath = chatCompletionsPath;
        ReasoningStateFormat = reasoningStateFormat;
        WriteReasoningContent = writeReasoningContent;
        WriteThinkingObject = writeThinkingObject;
        SupportsMinimalEffort = supportsMinimalEffort;
        MaximumEffortValue = maximumEffortValue;
        TokenLimit = tokenLimit;
        Faults = faults;
    }

    public string VendorName { get; }

    public string ChatCompletionsPath { get; }

    public string? ReasoningStateFormat { get; }

    public bool WriteReasoningContent { get; }

    public bool WriteThinkingObject { get; }

    public bool SupportsMinimalEffort { get; }

    public string MaximumEffortValue { get; }

    public CompatibleTokenLimit TokenLimit { get; }

    public CompatibleFaults Faults { get; }
}
