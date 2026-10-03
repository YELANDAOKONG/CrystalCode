using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class GeminiPlugin : IPlugin
{
    public string Name => "gemini";

    public PluginContribution Contribute() =>
        new(clients: [new GeminiClientFactory()]);
}
