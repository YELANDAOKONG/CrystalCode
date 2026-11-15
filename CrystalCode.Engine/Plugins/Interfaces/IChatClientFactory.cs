using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Plugins.Interfaces;

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

    /// <summary>
    /// Builds a client for a resolved credential. A factory that cannot send
    /// requests through an integration handler refuses one, so a login is
    /// never silently dropped.
    /// </summary>
    IStreamingChatClient Create(HarnessSettings settings, ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        RejectHandler(credential);
        return Create(settings, credential.ApiKey);
    }

    IStreamingMultimodalChatClient? CreateMultimodal(
        HarnessSettings settings,
        ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        RejectHandler(credential);
        return CreateMultimodal(settings, credential.ApiKey);
    }

    private static void RejectHandler(ProviderCredential credential)
    {
        if (credential.Handler is not null)
        {
            throw new NotSupportedException(
                "This client factory does not support account logins.");
        }
    }
}
