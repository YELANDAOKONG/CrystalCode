using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// The two tools that expose MCP resources. They are host tools: one pair for
/// every connected server, so the tool count does not grow with the servers.
/// </summary>
internal static class McpResourceTools
{
    public const string ListToolName = "list_mcp_resources";

    public const string ReadToolName = "read_mcp_resource";

    private const int MaximumListed = 200;

    public static IReadOnlyList<ITool> Create(McpResourceClients clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        return clients.Names.Count == 0
            ? []
            : [new ListTool(clients), new ReadTool(clients)];
    }

    private static string Servers(McpResourceClients clients) =>
        string.Join(", ", clients.Names);

    private static bool TryServer(
        McpResourceClients clients,
        string arguments,
        out McpResourceClients.Entry entry,
        out string name,
        out ToolOutput failure)
    {
        entry = null!;
        failure = null!;
        if (!ToolArguments.TryReadRequiredString(arguments, "server", out name))
        {
            failure = Failed("Argument 'server' is required.");
            return false;
        }

        if (!clients.TryGet(name, out entry))
        {
            failure = Failed($"No connected MCP server named '{name}' offers resources. Servers: {Servers(clients)}.");
            return false;
        }

        return true;
    }

    private static ToolOutput Failed(string text) => new(text, ToolResultStatus.Failure);

    private static CancellationTokenSource Limit(int? seconds, CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (seconds is { } limit)
        {
            source.CancelAfter(TimeSpan.FromSeconds(limit));
        }

        return source;
    }

    private sealed class ListTool : ITool
    {
        private readonly McpResourceClients _clients;

        public ListTool(McpResourceClients clients)
        {
            _clients = clients;
            Definition = ToolDefinition.Json(
                ListToolName,
                ToolSchema.Parse(
                    """
                    {"type":"object","properties":{"server":{"type":"string","description":"Name of a connected MCP server."}},"required":["server"]}
                    """),
                "Lists the resources an MCP server offers. Servers: " + Servers(clients) + ".");
        }

        public ToolDefinition Definition { get; }

        public async ValueTask<ToolOutput> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(call);
            if (!TryServer(_clients, call.Arguments, out var entry, out var name, out var failure))
            {
                return failure;
            }

            using var limit = Limit(entry.TimeoutSeconds, cancellationToken);
            try
            {
                var resources = await entry.Client.ListResourcesAsync(cancellationToken: limit.Token);
                if (resources.Count == 0)
                {
                    return new ToolOutput($"MCP server '{name}' offers no resources.", ToolResultStatus.Success);
                }

                var lines = resources
                    .Take(MaximumListed)
                    .Select(resource => Describe(resource.ProtocolResource))
                    .ToList();
                if (resources.Count > MaximumListed)
                {
                    lines.Add($"[{resources.Count - MaximumListed} more resources not shown]");
                }

                return new ToolOutput(
                    ToolOutputText.Truncate(string.Join(ToolOutputText.LineSeparator, lines)),
                    ToolResultStatus.Success);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failed(ToolOutputText.Timeout(entry.TimeoutSeconds ?? 0));
            }
            catch (Exception exception) when (exception is McpException or IOException)
            {
                return Failed($"MCP server '{name}' could not list resources. {exception.Message}");
            }
        }

        private static string Describe(Resource resource)
        {
            var builder = new StringBuilder(resource.Uri);
            if (!string.IsNullOrWhiteSpace(resource.Name))
            {
                builder.Append("  ").Append(resource.Name);
            }

            if (!string.IsNullOrWhiteSpace(resource.MimeType))
            {
                builder.Append("  ").Append(resource.MimeType);
            }

            if (!string.IsNullOrWhiteSpace(resource.Description))
            {
                builder.Append("  ").Append(resource.Description.ReplaceLineEndings(" "));
            }

            return builder.ToString();
        }
    }

    private sealed class ReadTool : ITool
    {
        private readonly McpResourceClients _clients;

        public ReadTool(McpResourceClients clients)
        {
            _clients = clients;
            Definition = ToolDefinition.Json(
                ReadToolName,
                ToolSchema.Parse(
                    """
                    {"type":"object","properties":{"server":{"type":"string","description":"Name of a connected MCP server."},"uri":{"type":"string","description":"Resource URI from list_mcp_resources."}},"required":["server","uri"]}
                    """),
                "Reads one resource from an MCP server. Servers: " + Servers(clients) + ".");
        }

        public ToolDefinition Definition { get; }

        public async ValueTask<ToolOutput> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(call);
            if (!TryServer(_clients, call.Arguments, out var entry, out var name, out var failure))
            {
                return failure;
            }

            if (!ToolArguments.TryReadRequiredString(call.Arguments, "uri", out var uri))
            {
                return Failed("Argument 'uri' is required.");
            }

            using var limit = Limit(entry.TimeoutSeconds, cancellationToken);
            try
            {
                var result = await entry.Client.ReadResourceAsync(uri, cancellationToken: limit.Token);
                var parts = result.Contents.Select(Describe).Where(part => part.Length > 0).ToList();
                var text = parts.Count == 0
                    ? $"MCP server '{name}' returned no content for '{uri}'."
                    : string.Join(ToolOutputText.LineSeparator, parts);
                return new ToolOutput(ToolOutputText.Truncate(text), ToolResultStatus.Success);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failed(ToolOutputText.Timeout(entry.TimeoutSeconds ?? 0));
            }
            catch (Exception exception) when (exception is McpException or IOException)
            {
                return Failed($"MCP server '{name}' could not read the resource. {exception.Message}");
            }
        }

        // Binary content is not sent to the model; naming it lets the model say so.
        private static string Describe(ResourceContents contents) =>
            contents switch
            {
                TextResourceContents text => text.Text ?? string.Empty,
                BlobResourceContents blob =>
                    $"[binary resource omitted: {blob.Uri} ({(string.IsNullOrWhiteSpace(blob.MimeType) ? "unknown type" : blob.MimeType)})]",
                _ => string.Empty
            };
    }
}
