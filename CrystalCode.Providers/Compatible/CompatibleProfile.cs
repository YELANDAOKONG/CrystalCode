namespace CrystalCode.Providers.Compatible;

internal sealed record CompatibleProfile
{
    public CompatibleProfile(
        string vendorName,
        string chatCompletionsPath,
        string? reasoningStateFormat,
        bool writeReasoningContent,
        bool writeThinkingObject,
        CompatibleTokenLimit tokenLimit,
        CompatibleFaults faults,
        CompatibleProcessing processing = CompatibleProcessing.Unsupported,
        bool requireAssistantContent = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatCompletionsPath);
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
        TokenLimit = tokenLimit;
        Faults = faults;
        Processing = processing;
        RequireAssistantContent = requireAssistantContent;
    }

    public string VendorName { get; }

    public string ChatCompletionsPath { get; }

    public string? ReasoningStateFormat { get; }

    public bool WriteReasoningContent { get; }

    public bool WriteThinkingObject { get; }

    public CompatibleTokenLimit TokenLimit { get; }

    public CompatibleFaults Faults { get; }

    public CompatibleProcessing Processing { get; }

    public bool RequireAssistantContent { get; }
}
