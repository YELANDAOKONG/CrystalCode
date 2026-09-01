using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Plugins.Providers;

public sealed class ResponsesPlugin : IPlugin
{
    public string Name => "responses";

    public PluginContribution Contribute() =>
        new(clients: [new ResponsesClientFactory()]);
}
