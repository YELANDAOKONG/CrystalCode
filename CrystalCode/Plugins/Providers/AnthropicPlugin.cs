using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Plugins.Providers;

public sealed class AnthropicPlugin : IPlugin
{
    public string Name => "anthropic";

    public PluginContribution Contribute() =>
        new(clients: [new AnthropicClientFactory()]);
}
