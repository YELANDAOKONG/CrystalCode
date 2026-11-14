using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.DeepSeek;

/// <summary>DeepSeek Chat Completions adapter with typed image input.</summary>
public sealed class DeepSeekMultimodalProvider
    : IStreamingMultimodalChatClient, IDisposable
{
    private readonly CompatibleMultimodalChatClient _client;

    public DeepSeekMultimodalProvider(DeepSeekOptions options, HttpClient? httpClient = null)
        : this(options, httpClient, null)
    {
    }

    public DeepSeekMultimodalProvider(
        DeepSeekOptions options,
        HttpClient? httpClient,
        IReadOnlyDictionary<string, string>? httpHeaders)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new CompatibleMultimodalChatClient(
            DeepSeekProvider.CreateProfile(),
            options.ToCompatibleOptions(),
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
