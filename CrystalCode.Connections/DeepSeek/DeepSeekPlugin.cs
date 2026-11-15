using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Connections.DeepSeek;

/// <summary>
/// Registers the DeepSeek chat-client factory.
/// </summary>
public sealed class DeepSeekPlugin : IPlugin
{
    public string Name => "deepseek";

    public PluginContribution Contribute() =>
        new(clients: [new DeepSeekClientFactory()]);
}
