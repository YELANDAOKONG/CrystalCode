using Crystal.Chat;
using Crystal.Multimodal.Chat;

using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class ScriptedChatClientFactory : IChatClientFactory
{
    private readonly IStreamingChatClient _client;
    private readonly IStreamingMultimodalChatClient? _images;
    private readonly Action<ProviderRequestContext>? _observeRequest;

    public ScriptedChatClientFactory(
        IStreamingChatClient client,
        IStreamingMultimodalChatClient? images = null,
        Action<ProviderRequestContext>? observeRequest = null)
    {
        _images = images;
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _observeRequest = observeRequest;
    }

    public int Creates { get; private set; }

    public List<int?> OutputCaps { get; } = [];

    /// <summary>
    /// Model, service tier, and speed of each created client, in order.
    /// </summary>
    public List<(string Model, string? ServiceTier, string? Speed)> Slots { get; } = [];

    public List<(string Model, string? ServiceTier, string? Speed)> MultimodalSlots { get; } = [];

    public bool CanCreate(ProviderProtocol protocol) =>
        protocol == ScriptedChatPlugin.Protocol;

    public IStreamingMultimodalChatClient? CreateMultimodal(HarnessSettings settings, string apiKey)
    {
        MultimodalSlots.Add((settings.Model, settings.ServiceTier, settings.Speed));
        return _images;
    }

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        Creates++;
        OutputCaps.Add(settings.MaxOutputTokens);
        Slots.Add((settings.Model, settings.ServiceTier, settings.Speed));
        return _client;
    }

    public IStreamingChatClient Create(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext> requestContext)
    {
        var client = Create(settings, credential.ApiKey);
        return _observeRequest is { } observe ? new ObservedClient(client, requestContext, observe) : client;
    }

    private sealed class ObservedClient(
        IStreamingChatClient client,
        Func<ProviderRequestContext> context,
        Action<ProviderRequestContext> observe) : IStreamingChatClient
    {
        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            observe(context());
            return client.CompleteAsync(request, cancellationToken);
        }

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            observe(context());
            return client.StreamAsync(request, cancellationToken);
        }
    }
}
