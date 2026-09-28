using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Ollama;

public sealed class OllamaMultimodalProvider : IStreamingMultimodalChatClient, IDisposable
{
    private readonly ProtocolMultimodalChatClient _client;

    public OllamaMultimodalProvider(OllamaOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new ProtocolMultimodalChatClient(
            new OllamaMultimodalCodec(),
            options.ToProtocolOptions(),
            httpClient);
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
