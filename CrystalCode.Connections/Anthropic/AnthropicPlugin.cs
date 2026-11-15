using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Connections.Anthropic;

public sealed class AnthropicPlugin : IPlugin
{
    public string Name => "anthropic";

    public PluginContribution Contribute() =>
        new(clients: [new AnthropicClientFactory()]);
}
