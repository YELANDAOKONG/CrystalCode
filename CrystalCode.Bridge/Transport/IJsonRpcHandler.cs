using System.Text.Json;

namespace CrystalCode.Bridge.Transport;

/// <summary>
/// Answers what the client sends. A request handler returns the reply or
/// throws <see cref="JsonRpcException"/>; the connection writes either.
/// </summary>
internal interface IJsonRpcHandler
{
    Task<JsonRpcReply> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken);

    Task HandleNotificationAsync(string method, JsonElement parameters);

    /// <summary>Stdin closed or the process is stopping.</summary>
    Task ShutdownAsync();
}
