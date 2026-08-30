using Crystal.Chat;

using CrystalHarness.Providers.Compatible;

namespace CrystalHarness.Providers.DeepSeek;

/// <summary>
/// DeepSeek chat adapter for Crystal text-chat contracts.
/// </summary>
public sealed class DeepSeekProvider : IStreamingChatClient, IDisposable
{
    internal const string ReasoningStateFormat = "deepseek.reasoning_content";

    private readonly CompatibleChatClient _client;

    /// <summary>
    /// Initializes a DeepSeek chat adapter.
    /// </summary>
    /// <param name="options">The configured DeepSeek options.</param>
    /// <param name="httpClient">
    /// Optional caller-owned HTTP client. When omitted, the adapter creates and
    /// disposes its own client.
    /// </param>
    public DeepSeekProvider(DeepSeekOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var profile = new CompatibleProfile(
            vendorName: "DeepSeek",
            chatCompletionsPath: CompatibleWire.ChatCompletionsPath,
            reasoningStateFormat: ReasoningStateFormat,
            writeReasoningContent: true,
            writeThinkingObject: true,
            supportsMinimalEffort: false,
            maximumEffortValue: "max",
            tokenLimit: CompatibleTokenLimit.MaxTokens,
            faults: new CompatibleFaults(
                typeof(DeepSeekException),
                static (message, statusCode, inner) =>
                    new DeepSeekException(message, statusCode, inner)));

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
