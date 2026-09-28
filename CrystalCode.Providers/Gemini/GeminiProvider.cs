using Crystal.Chat;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Gemini;

public sealed class GeminiProvider : IStreamingChatClient, IDisposable
{
    private readonly ProtocolChatClient _client;

    public GeminiProvider(GeminiOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new ProtocolChatClient(
            new GeminiCodec(options.Model),
            options.ToProtocolOptions(),
            httpClient);
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
