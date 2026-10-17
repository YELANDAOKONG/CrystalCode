using Crystal.Chat;

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

    public ScriptedChatPlugin(IStreamingChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _factory = new ScriptedChatClientFactory(client);
    }

    public int Creates => _factory.Creates;

    public string Name => "scripted";

    public PluginContribution Contribute() =>
        new(clients: [_factory]);
}
