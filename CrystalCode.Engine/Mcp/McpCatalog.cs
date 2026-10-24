using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;

using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// The MCP servers a session is connected to and the tools they provide.
/// Dispose it to close every connection.
/// </summary>
internal sealed class McpCatalog : IAsyncDisposable
{
    private const int ListToolsTimeoutSeconds = 30;

    private readonly List<McpConnection> _connections;

    private McpCatalog(
        List<McpConnection> connections,
        IReadOnlyList<ITool> planTools,
        IReadOnlyList<ITool> workTools,
        IReadOnlyList<McpServerInfo> servers,
        IReadOnlyList<McpToolInfo> tools,
        IReadOnlyList<string> notes)
    {
        _connections = connections;
        PlanTools = planTools;
        WorkTools = workTools;
        Servers = servers;
        Tools = tools;
        Notes = notes;
        var all = tools.Select(tool => tool.ModelName).ToHashSet(StringComparer.Ordinal);
        var readOnly = tools.Where(tool => tool.ReadOnly)
            .Select(tool => tool.ModelName)
            .ToHashSet(StringComparer.Ordinal);
        Classifier = new McpApprovalClassifier(all, readOnly);
    }

    public static McpCatalog Empty { get; } = new([], [], [], [], [], []);

    public IReadOnlyList<ITool> PlanTools { get; }

    public IReadOnlyList<ITool> WorkTools { get; }

    public IReadOnlyList<McpServerInfo> Servers { get; }

    public IReadOnlyList<McpToolInfo> Tools { get; }

    /// <summary>
    /// Problems the operator should see: servers that did not connect and
    /// tools that were skipped. Messages never contain configuration values.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    public IApprovalClassifier Classifier { get; }

    /// <summary>
    /// Connects every definition at the same time and waits until each one
    /// has connected or failed. A failed server is reported in
    /// <see cref="Notes"/> and does not stop the others.
    /// </summary>
    /// <param name="reservedNames">
    /// Tool names already taken by other catalogs.
    /// </param>
    public static async Task<McpCatalog> ConnectAsync(
        IReadOnlyList<McpServerDefinition> definitions,
        string workspaceRoot,
        McpToolApprovalSettings approval,
        IReadOnlySet<string> reservedNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(reservedNames);
        if (definitions.Count == 0)
        {
            return Empty;
        }

        return await LoadConnectionsAsync(
            [.. definitions.Select(definition => McpConnection.Connect(definition, workspaceRoot))],
            approval,
            reservedNames,
            cancellationToken);
    }

    /// <summary>
    /// Takes ownership of <paramref name="connections"/>: it disposes the
    /// failed ones now and the rest when the catalog is disposed.
    /// </summary>
    internal static async Task<McpCatalog> LoadConnectionsAsync(
        List<McpConnection> connections,
        McpToolApprovalSettings approval,
        IReadOnlySet<string> reservedNames,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var loads = await Task.WhenAll(
                connections.Select(connection => LoadAsync(connection, cancellationToken)));
            return await AssembleAsync(loads, approval, reservedNames);
        }
        catch (Exception)
        {
            await DisposeAllAsync(connections);
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeAllAsync(_connections);

    private static async Task<ServerLoad> LoadAsync(
        McpConnection connection,
        CancellationToken cancellationToken)
    {
        var ready = await connection.WaitUntilReadyAsync(cancellationToken);
        if (ready.Client is null)
        {
            return new ServerLoad(connection, null, ready.Error);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ListToolsTimeoutSeconds));
        try
        {
            var tools = await ready.Client.ListToolsAsync(cancellationToken: timeout.Token);
            return new ServerLoad(connection, [.. tools], string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ServerLoad(
                connection,
                null,
                $"MCP server '{connection.Definition.Name}' did not list its tools within "
                + $"{ListToolsTimeoutSeconds} seconds.");
        }
        catch (Exception exception) when (exception is McpException or IOException)
        {
            return new ServerLoad(
                connection,
                null,
                $"MCP server '{connection.Definition.Name}' could not list its tools: {exception.Message}");
        }
    }

    private static async Task<McpCatalog> AssembleAsync(
        IReadOnlyList<ServerLoad> loads,
        McpToolApprovalSettings approval,
        IReadOnlySet<string> reservedNames)
    {
        var kept = new List<McpConnection>();
        var plan = new List<ITool>();
        var work = new List<ITool>();
        var servers = new List<McpServerInfo>();
        var infos = new List<McpToolInfo>();
        var notes = new List<string>();
        var taken = new HashSet<string>(reservedNames, StringComparer.Ordinal);

        foreach (var load in loads)
        {
            var definition = load.Connection.Definition;
            if (load.Tools is null)
            {
                notes.Add(load.Error);
                servers.Add(new McpServerInfo(
                    definition.Name,
                    definition.Source,
                    definition.Transport,
                    McpConnectionStatus.Failed,
                    0,
                    load.Error));
                await load.Connection.DisposeAsync();
                continue;
            }

            kept.Add(load.Connection);
            var client = (await load.Connection.WaitUntilReadyAsync()).Client!;
            var trusted = approval.For(definition.Source) == ExternalToolTrustPolicy.Author;
            var count = 0;
            foreach (var remote in load.Tools.OrderBy(tool => tool.Name, StringComparer.Ordinal))
            {
                if (!McpServerNames.TryToolName(definition.Name, remote.Name, out var modelName))
                {
                    notes.Add(
                        $"MCP tool '{remote.Name}' from '{definition.Name}' was skipped: the name is too long or empty.");
                    continue;
                }

                if (!taken.Add(modelName))
                {
                    notes.Add(
                        $"MCP tool '{remote.Name}' from '{definition.Name}' was skipped: "
                        + $"the tool name '{modelName}' is already in use.");
                    continue;
                }

                var tool = new McpTool(
                    modelName,
                    definition.Name,
                    remote.ProtocolTool,
                    client,
                    definition.ToolTimeoutSeconds);
                if (definition.InPlan)
                {
                    plan.Add(tool);
                }

                if (definition.InWork)
                {
                    work.Add(tool);
                }

                infos.Add(new McpToolInfo(
                    modelName,
                    definition.Name,
                    remote.Name,
                    trusted && remote.ProtocolTool.Annotations?.ReadOnlyHint == true,
                    definition.InPlan,
                    definition.InWork));
                count++;
            }

            servers.Add(new McpServerInfo(
                definition.Name,
                definition.Source,
                definition.Transport,
                McpConnectionStatus.Connected,
                count,
                string.Empty));
        }

        return new McpCatalog(kept, plan, work, servers, infos, notes);
    }

    private static async Task DisposeAllAsync(IEnumerable<McpConnection> connections)
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private sealed record ServerLoad(
        McpConnection Connection,
        IReadOnlyList<McpClientTool>? Tools,
        string Error);
}
