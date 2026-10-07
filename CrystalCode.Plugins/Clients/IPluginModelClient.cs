namespace CrystalCode.Plugins.Clients;

/// <summary>
/// Opt-in for calling the session model and, when review uses its own
/// model, the review model. Implementing this interface is what tells the
/// host to hand over clients. <see cref="IPluginClientFactory"/> is the
/// other direction: a plugin builds a client for a protocol the host does
/// not already own.
/// </summary>
public interface IPluginModelClient
{
    /// <summary>
    /// Receives the side-channel clients. The host calls this after
    /// <c>AttachSession</c>, and again when this plugin instance is loaded
    /// again.
    /// </summary>
    void AttachClients(IPluginClients clients);
}
