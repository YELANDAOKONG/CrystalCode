using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One tool a connected MCP server provides, exposed to the model under a
/// Crystal-owned name.
/// </summary>
internal sealed class McpTool : ITool
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly string _remoteName;
    private readonly int? _timeoutSeconds;

    public McpTool(
        string modelName,
        string serverName,
        Tool remote,
        McpClient client,
        int? timeoutSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _serverName = serverName;
        _remoteName = remote.Name;
        _timeoutSeconds = timeoutSeconds;
        var description = string.IsNullOrWhiteSpace(remote.Description)
            ? remote.Title
            : remote.Description;
        Definition = ToolDefinition.Json(modelName, remote.InputSchema, description);
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!TryReadArguments(call.Arguments, out var arguments))
        {
            return Failed("The tool arguments must be a JSON object.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeoutSeconds is { } seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        try
        {
            var result = await _client.CallToolAsync(
                new CallToolRequestParams { Name = _remoteName, Arguments = arguments },
                timeout.Token);
            return McpToolResultText.ToOutput(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(ToolOutputText.Timeout(_timeoutSeconds ?? 0));
        }
        catch (Exception exception) when (exception is McpException or IOException)
        {
            return Failed(exception.Message);
        }
    }

    private ToolOutput Failed(string message) =>
        new($"MCP server '{_serverName}' tool '{_remoteName}' failed. {message}", ToolResultStatus.Failure);

    private static bool TryReadArguments(
        string json,
        out Dictionary<string, JsonElement>? arguments)
    {
        arguments = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            arguments = document.RootElement.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.Clone(),
                    StringComparer.Ordinal);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
