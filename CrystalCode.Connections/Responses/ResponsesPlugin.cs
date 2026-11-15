using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Connections.Responses;

public sealed class ResponsesPlugin : IPlugin
{
    public string Name => "responses";

    public PluginContribution Contribute() =>
        new(clients: [new ResponsesClientFactory()]);
}
