using ModelContextProtocol.Client;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// The connected servers that offer resources, by server name. The two
/// resource tools share one instance, and the catalog replaces it on reload.
/// </summary>
internal sealed class McpResourceClients
{
    public static McpResourceClients Empty { get; } = new(new Dictionary<string, Entry>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, Entry> _servers;

    public McpResourceClients(IReadOnlyDictionary<string, Entry> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        _servers = servers;
    }

    public IReadOnlyList<string> Names => [.. _servers.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    public bool TryGet(string name, out Entry entry)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _servers.TryGetValue(name, out entry!);
    }

    internal sealed record Entry(McpClient Client, int? TimeoutSeconds);
}
