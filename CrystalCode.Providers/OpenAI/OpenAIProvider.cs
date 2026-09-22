using Crystal.Chat;
using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.OpenAI;

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

        _client = new CompatibleChatClient(CreateProfile(options), options.ToCompatibleOptions(), httpClient);
    }

    internal static CompatibleProfile CreateProfile(OpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new CompatibleProfile(
            vendorName: options.VendorName,
            chatCompletionsPath: CompatibleWire.ChatCompletionsPath,
            reasoningStateFormat: ReasoningStateFormat,
            writeReasoningContent: options.ReplayReasoningContent,
            writeThinkingObject: false,
            supportsMinimalEffort: true,
            maximumEffortValue: "xhigh",
            tokenLimit: options.UseMaxCompletionTokens
                ? CompatibleTokenLimit.MaxCompletionTokens
                : CompatibleTokenLimit.MaxTokens,
            faults: new CompatibleFaults(
                typeof(OpenAIException),
                static (message, statusCode, inner, errorCode, retryAfter) =>
                    new OpenAIException(message, statusCode, inner, errorCode, retryAfter)));
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
