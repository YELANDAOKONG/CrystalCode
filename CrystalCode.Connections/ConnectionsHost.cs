using CrystalCode.Connections.Anthropic;
using CrystalCode.Connections.DeepSeek;
using CrystalCode.Connections.Gemini;
using CrystalCode.Connections.Ollama;
using CrystalCode.Connections.OpenAI;
using CrystalCode.Connections.Responses;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Connections;

/// <summary>
/// Registers this assembly's provider definitions and client factories with
/// the host.
/// </summary>
public static class ConnectionsHost
{
    public const string Key = "connections";

    public static HostContribution CreateContribution() => new(
        Key,
        BuiltInProviders.Create(),
        [
            static () => new DeepSeekPlugin(),
            static () => new OpenAIPlugin(),
            static () => new ResponsesPlugin(),
            static () => new AnthropicPlugin(),
            static () => new GeminiPlugin(),
            static () => new OllamaPlugin()
        ],
        ProviderNames.DeepSeek,
        BuiltInProviders.DefaultModel);

    public static void Register() => HostDefaults.Register(CreateContribution());
}
