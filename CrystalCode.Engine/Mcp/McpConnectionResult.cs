using ModelContextProtocol.Client;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// The outcome of one connection attempt.
/// </summary>
internal sealed record McpConnectionResult
{
    private McpConnectionResult(McpClient? client, string error)
    {
        Client = client;
        Error = error;
    }

    public McpClient? Client { get; }

    public string Error { get; }

    public static McpConnectionResult Success(McpClient client) =>
        new(client, string.Empty);

    public static McpConnectionResult Failure(string error) =>
        new(null, error);
}
