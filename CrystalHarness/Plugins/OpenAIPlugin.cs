namespace CrystalHarness.Plugins;

/// <summary>
/// Registers the OpenAI-compatible chat-client factory.
/// </summary>
public sealed class OpenAIPlugin : IPlugin
{
    public string Name => "openai";

    public PluginContribution Contribute() =>
        new(clients: [new OpenAIClientFactory()]);
}
