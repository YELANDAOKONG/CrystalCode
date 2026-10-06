using Crystal.Chat;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Plugins.Clients;

/// <summary>
/// Builds a streaming chat client for one provider protocol that the built-in
/// adapters do not already own.
/// </summary>
public interface IPluginClientFactory
{
    /// <summary>Returns whether this factory handles the protocol token.</summary>
    bool CanCreate(string protocol);

    /// <summary>Creates the text client for one request.</summary>
    IStreamingChatClient Create(PluginClientRequest request);

    /// <summary>Creates the image-input client, or null when this protocol is text only.</summary>
    IStreamingMultimodalChatClient? CreateMultimodal(PluginClientRequest request) => null;
}
