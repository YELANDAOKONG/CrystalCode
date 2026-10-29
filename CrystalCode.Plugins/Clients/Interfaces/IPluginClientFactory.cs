using Crystal.Chat;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Plugins.Clients.Interfaces;
/// <summary>
/// Builds a streaming chat client for one provider protocol. A built-in
/// protocol is refused unless <see cref="OverridesBuiltIn"/> is true.
/// </summary>
public interface IPluginClientFactory
{
    /// <summary>Returns whether this factory handles the protocol token.</summary>
    bool CanCreate(string protocol);

    /// <summary>
    /// Gets whether this factory is asked before the built-in adapters, so it
    /// can take over a built-in protocol. The host asks such factories in
    /// plugin load order and uses the first one that returns true.
    /// </summary>
    bool OverridesBuiltIn => false;

    /// <summary>Creates the text client for one request.</summary>
    IStreamingChatClient Create(PluginClientRequest request);

    /// <summary>Creates the image-input client, or null when this protocol is text only.</summary>
    IStreamingMultimodalChatClient? CreateMultimodal(PluginClientRequest request) => null;
}
