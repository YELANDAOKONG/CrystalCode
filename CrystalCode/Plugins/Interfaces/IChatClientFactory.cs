using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Configuration;

namespace CrystalCode.Plugins.Interfaces;

/// <summary>
/// Builds a streaming chat client for one provider protocol.
/// </summary>
public interface IChatClientFactory
{
    bool CanCreate(ProviderProtocol protocol);

    IStreamingChatClient Create(HarnessSettings settings, string apiKey);

    IStreamingMultimodalChatClient? CreateMultimodal(
        HarnessSettings settings,
        string apiKey) => null;
}
