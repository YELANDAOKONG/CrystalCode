using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class ResponsesPlugin : IPlugin
{
    public string Name => "responses";

    public PluginContribution Contribute() =>
        new(clients: [new ResponsesClientFactory()]);
}
