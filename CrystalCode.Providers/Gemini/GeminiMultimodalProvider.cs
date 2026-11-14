using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Gemini;

public sealed class GeminiMultimodalProvider : IStreamingMultimodalChatClient, IDisposable
{
    private readonly ProtocolMultimodalChatClient _client;

    public GeminiMultimodalProvider(GeminiOptions options, HttpClient? httpClient = null)
        : this(options, httpClient, null)
    {
    }

    public GeminiMultimodalProvider(
        GeminiOptions options,
        HttpClient? httpClient,
        IReadOnlyDictionary<string, string>? httpHeaders)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new ProtocolMultimodalChatClient(
            new GeminiMultimodalCodec(options.Model),
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
