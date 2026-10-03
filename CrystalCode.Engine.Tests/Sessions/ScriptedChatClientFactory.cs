using Crystal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class ScriptedChatClientFactory : IChatClientFactory
{
    private readonly IStreamingChatClient _client;

    public ScriptedChatClientFactory(IStreamingChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public bool CanCreate(ProviderProtocol protocol) =>
        protocol == ScriptedChatPlugin.Protocol;

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey) => _client;
}
