using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class OllamaPlugin : IPlugin
{
    public string Name => "ollama";

    public PluginContribution Contribute() =>
        new(clients: [new OllamaClientFactory()]);
}
