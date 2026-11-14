using Crystal.Chat;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Ollama;

public sealed class OllamaProvider : IStreamingChatClient, IDisposable
{
    private readonly ProtocolChatClient _client;

    public OllamaProvider(OllamaOptions options, HttpClient? httpClient = null)
        : this(options, httpClient, null)
    {
    }

    public OllamaProvider(
        OllamaOptions options,
        HttpClient? httpClient,
        IReadOnlyDictionary<string, string>? httpHeaders)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new ProtocolChatClient(new OllamaCodec(), options.ToProtocolOptions(), httpClient, httpHeaders);
    }

    public Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default) =>
        _client.CompleteAsync(request, cancellationToken);

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default) =>
        _client.StreamAsync(request, cancellationToken);

    public void Dispose() => _client.Dispose();
}
