using CrystalCode.Server.Hosting;
using CrystalCode.Server.Security;

namespace CrystalCode.Server.Endpoints;

/// <summary>Shared services the endpoints use.</summary>
public sealed record ServerContext(
    ServerOptions Options,
    ServerRuntime Runtime,
    ServerSession Session,
    TokenStore? Tokens,
    ClientRegistry Clients,
    RequestLedger Requests,
    Action Shutdown);
