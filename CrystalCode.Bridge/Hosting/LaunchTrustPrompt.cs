using System.Text.Json;

using CrystalCode.Bridge.Transport;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Interfaces;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// Asks the client to trust a workspace before a session exists, as a form
/// tied to the <c>session/new</c>, <c>session/load</c>, or
/// <c>session/resume</c> request that needs it.
/// </summary>
internal sealed class LaunchTrustPrompt(JsonRpcConnection connection, JsonElement requestId) : IWorkspaceTrustPrompt
{
    public async ValueTask<bool> ConfirmAsync(WorkspaceTrustRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await BridgeSession.AskFormAsync(
            connection,
            writer => FormWire.Trust(writer, null, requestId, request),
            cancellationToken);
        return result is { } value && FormWire.Trusted(value);
    }
}
