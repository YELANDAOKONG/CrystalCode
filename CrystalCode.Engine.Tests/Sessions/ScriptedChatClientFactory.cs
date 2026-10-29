using Crystal.Chat;
using Crystal.Multimodal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class ScriptedChatClientFactory : IChatClientFactory
{
    private readonly IStreamingChatClient _client;
    private readonly IStreamingMultimodalChatClient? _images;

    public ScriptedChatClientFactory(IStreamingChatClient client, IStreamingMultimodalChatClient? images = null)
    {
        _images = images;
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
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
}
