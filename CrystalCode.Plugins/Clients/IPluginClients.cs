using Crystal.Chat;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Plugins.Clients;

/// <summary>
/// Side-channel clients for plugin calls. They are not the instances inside
/// a turn or a review. Read a property again after the host rebuilds that
/// client; it drops the previous instance.
/// </summary>
public interface IPluginClients
{
    /// <summary>Text client for the session model.</summary>
    IStreamingChatClient Session { get; }

    /// <summary>
    /// Image-input client for the session model, or null when that model
    /// does not accept images.
    /// </summary>
    IStreamingMultimodalChatClient? Images { get; }

    /// <summary>
    /// True when review calls its own model. False when review calls the
    /// session model. Reading this does not create a client.
    /// </summary>
    bool IndependentReview { get; }

    /// <summary>
    /// Text client for the review model while <see cref="IndependentReview"/>
    /// is true. Null while review uses the session model. When review has
    /// its own model and that client cannot be created, the read fails and
    /// the session client is left untouched.
    /// </summary>
    IStreamingChatClient? Review { get; }
}
