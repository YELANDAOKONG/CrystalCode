using Crystal.Chat;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Responses;

public sealed class ResponsesProvider : IStreamingChatClient, IDisposable
{
    private readonly ProtocolChatClient _client;

    public ResponsesProvider(ResponsesOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new ProtocolChatClient(
            new ResponsesCodec(options.VendorName),
            options.ToProtocolOptions(),
            httpClient);
    }

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        _client.CompleteAsync(request, cancellationToken);

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        _client.StreamAsync(request, cancellationToken);

    public void Dispose() => _client.Dispose();
}
