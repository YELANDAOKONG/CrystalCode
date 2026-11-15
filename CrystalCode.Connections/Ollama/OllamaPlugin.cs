using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Connections.Ollama;

public sealed class OllamaPlugin : IPlugin
{
    public string Name => "ollama";

    public PluginContribution Contribute() =>
        new(clients: [new OllamaClientFactory()]);
}
