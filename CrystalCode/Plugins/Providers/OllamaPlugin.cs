using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Plugins.Providers;

public sealed class OllamaPlugin : IPlugin
{
    public string Name => "ollama";

    public PluginContribution Contribute() =>
        new(clients: [new OllamaClientFactory()]);
}
