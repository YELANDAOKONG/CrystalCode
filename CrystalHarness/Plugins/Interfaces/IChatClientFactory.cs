using Crystal.Chat;

using CrystalHarness.Configuration;

namespace CrystalHarness.Plugins.Interfaces;

/// <summary>
/// Builds a streaming chat client for one provider protocol.
/// </summary>
public interface IChatClientFactory
{
    bool CanCreate(ProviderProtocol protocol);

    IStreamingChatClient Create(HarnessSettings settings, string apiKey);
}
