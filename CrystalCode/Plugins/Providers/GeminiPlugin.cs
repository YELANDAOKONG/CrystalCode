using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Plugins.Providers;

public sealed class GeminiPlugin : IPlugin
{
    public string Name => "gemini";

    public PluginContribution Contribute() =>
        new(clients: [new GeminiClientFactory()]);
}
