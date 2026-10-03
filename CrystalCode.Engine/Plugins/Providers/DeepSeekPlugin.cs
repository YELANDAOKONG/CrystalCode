using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Plugins.Providers;

/// <summary>
/// Registers the DeepSeek chat-client factory.
/// </summary>
public sealed class DeepSeekPlugin : IPlugin
{
    public string Name => "deepseek";

    public PluginContribution Contribute() =>
        new(clients: [new DeepSeekClientFactory()]);
}
