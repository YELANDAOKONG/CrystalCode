using Crystal.Chat;
using Crystal.Multimodal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Supplies a scripted chat client to a real session through the same plugin
/// table that production providers use.
/// </summary>
internal sealed class ScriptedChatPlugin : IPlugin
{
    /// <summary>
    /// A keyless built-in protocol, so no credential is needed.
    /// </summary>
    public static ProviderProtocol Protocol { get; } = ProviderProtocol.Ollama;

    private readonly ScriptedChatClientFactory _factory;

    public ScriptedChatPlugin(IStreamingChatClient client, IStreamingMultimodalChatClient? images = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _factory = new ScriptedChatClientFactory(client, images);
    }

    public int Creates => _factory.Creates;

    public IReadOnlyList<int?> OutputCaps => _factory.OutputCaps;

    public IReadOnlyList<(string Model, string? ServiceTier, string? Speed)> Slots => _factory.Slots;

    public IReadOnlyList<(string Model, string? ServiceTier, string? Speed)> MultimodalSlots =>
        _factory.MultimodalSlots;

    public string Name => "scripted";

    public PluginContribution Contribute() =>
        new(clients: [_factory]);
}
