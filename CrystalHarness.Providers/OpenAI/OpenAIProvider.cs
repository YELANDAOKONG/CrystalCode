using Crystal.Chat;

using CrystalHarness.Providers.Compatible;

namespace CrystalHarness.Providers.OpenAI;

/// <summary>
/// OpenAI Chat Completions adapter for Crystal text-chat contracts.
/// </summary>
public sealed class OpenAIProvider : IStreamingChatClient, IDisposable
{
    internal const string ReasoningStateFormat = "openai.reasoning_content";

    private readonly CompatibleChatClient _client;

    /// <summary>
    /// Initializes an OpenAI chat adapter.
    /// </summary>
    /// <param name="options">The configured OpenAI options.</param>
    /// <param name="httpClient">
    /// Optional caller-owned HTTP client. When omitted, the adapter creates and
    /// disposes its own client.
    /// </param>
    public OpenAIProvider(OpenAIOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var profile = new CompatibleProfile(
            vendorName: "OpenAI",
            chatCompletionsPath: CompatibleWire.ChatCompletionsPath,
            reasoningStateFormat: ReasoningStateFormat,
            writeReasoningContent: options.ReplayReasoningContent,
            writeThinkingObject: false,
            supportsMinimalEffort: true,
            maximumEffortValue: "xhigh",
            tokenLimit: CompatibleTokenLimit.MaxCompletionTokens,
            faults: new CompatibleFaults(
                typeof(OpenAIException),
                static (message, statusCode, inner) =>
                    new OpenAIException(message, statusCode, inner)));

        _client = new CompatibleChatClient(profile, options.ToCompatibleOptions(), httpClient);
    }

    /// <inheritdoc />
    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        return _client.CompleteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        return _client.StreamAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
