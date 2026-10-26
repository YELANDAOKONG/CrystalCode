namespace CrystalCode.Server.Hosting;

/// <summary>Where the server is in its life. Reported by /health and server.state.</summary>
public static class ServerState
{
    public const string Starting = "starting";

    public const string AwaitingOperator = "awaiting_operator";

    public const string Ready = "ready";

    public const string Closing = "closing";

    public const string Failed = "failed";
}
