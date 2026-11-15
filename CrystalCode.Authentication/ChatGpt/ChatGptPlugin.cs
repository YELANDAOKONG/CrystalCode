using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Authentication.ChatGpt;

public sealed class ChatGptPlugin : IPlugin
{
    public string Name => "chatgpt";

    public PluginContribution Contribute() =>
        new(integrations: [new ChatGptIntegration()]);
}
