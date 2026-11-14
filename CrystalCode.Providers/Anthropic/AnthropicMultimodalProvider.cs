using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Anthropic;

/// <summary>Anthropic Messages adapter with typed image input.</summary>
public sealed class AnthropicMultimodalProvider
    : IStreamingMultimodalChatClient, IDisposable
{
    private readonly ProtocolMultimodalChatClient _client;

    public AnthropicMultimodalProvider(AnthropicOptions options, HttpClient? httpClient = null)
        : this(options, httpClient, null)
    {
    }

    public AnthropicMultimodalProvider(
        AnthropicOptions options,
        HttpClient? httpClient,
        IReadOnlyDictionary<string, string>? httpHeaders)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new ProtocolMultimodalChatClient(
            new AnthropicMultimodalCodec(options.VendorName, options.SummarizeThinking),
            options.ToProtocolOptions(),
            httpClient, httpHeaders);
    }

    public MultimodalChatCapabilities Capabilities => _client.Capabilities;

    public Task<MultimodalChatResponse> CompleteAsync(
        MultimodalChatRequest request,
        CancellationToken cancellationToken = default) =>
        _client.CompleteAsync(request, cancellationToken);

    public IAsyncEnumerable<MultimodalChatStreamEvent> StreamAsync(
        MultimodalChatRequest request,
        CancellationToken cancellationToken = default) =>
        _client.StreamAsync(request, cancellationToken);

    public void Dispose() => _client.Dispose();
}
