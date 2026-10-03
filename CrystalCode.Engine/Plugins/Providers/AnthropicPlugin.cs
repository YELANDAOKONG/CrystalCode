using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class AnthropicPlugin : IPlugin
{
    public string Name => "anthropic";

    public PluginContribution Contribute() =>
        new(clients: [new AnthropicClientFactory()]);
}
