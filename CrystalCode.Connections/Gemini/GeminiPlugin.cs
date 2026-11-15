using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Connections.Gemini;

public sealed class GeminiPlugin : IPlugin
{
    public string Name => "gemini";

    public PluginContribution Contribute() =>
        new(clients: [new GeminiClientFactory()]);
}
