using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Connections.OpenAI;

/// <summary>
/// Registers the OpenAI-compatible chat-client factory.
/// </summary>
public sealed class OpenAIPlugin : IPlugin
{
    public string Name => "openai";

    public PluginContribution Contribute() =>
        new(clients: [new OpenAIClientFactory()]);
}
