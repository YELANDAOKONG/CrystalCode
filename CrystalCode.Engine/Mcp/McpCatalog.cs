using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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
        IReadOnlyList<string> notes,
        McpResourceClients resources,
        IReadOnlyList<McpPromptEntry> prompts)
    {
        _connections = connections;
        Resources = resources;
        Prompts = prompts;
        PlanTools = planTools;
        WorkTools = workTools;
        Servers = servers;
        Tools = tools;
        Notes = notes;
        var all = tools.Select(tool => tool.ModelName).ToHashSet(StringComparer.Ordinal);
        if (resources.Names.Count > 0)
        {
            all.Add(McpResourceTools.ListToolName);
            all.Add(McpResourceTools.ReadToolName);
        }

        var readOnly = tools.Where(tool => tool.ReadOnly)
            .Select(tool => tool.ModelName)
            .ToHashSet(StringComparer.Ordinal);
        Classifier = new McpApprovalClassifier(all, readOnly);
    }

    public static McpCatalog Empty { get; } = new(
        [], [], [], [], [], [], McpResourceClients.Empty, []);

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

    internal McpResourceClients Resources { get; }

    internal IReadOnlyList<McpPromptEntry> Prompts { get; }

    /// <summary>
    /// The two resource host tools, or none when no server offers resources.
    /// They are classified like the tools of the server they read from, so they
    /// are always at least a workspace write.
    /// </summary>
    public IReadOnlyList<ITool> ResourceTools => McpResourceTools.Create(Resources);

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
        CancellationToken cancellationToken = default,
        McpHostCallbacks? callbacks = null)
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
            [.. definitions.Select(definition => McpConnection.Connect(definition, workspaceRoot, callbacks))],
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
            var prompts = await ListPromptsAsync(ready.Client, timeout.Token);
            return new ServerLoad(connection, [.. tools], string.Empty, prompts);
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

    // A server without prompts is normal, and a failure here must not hide its tools.
    private static async Task<IReadOnlyList<Prompt>> ListPromptsAsync(
        McpClient client,
        CancellationToken cancellationToken)
    {
        if (client.ServerCapabilities?.Prompts is null)
        {
            return [];
        }

        try
        {
            return [.. (await client.ListPromptsAsync(cancellationToken: cancellationToken)).Select(item => item.ProtocolPrompt)];
        }
        catch (Exception exception) when (exception is McpException or IOException)
        {
            return [];
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
        var resources = new Dictionary<string, McpResourceClients.Entry>(StringComparer.Ordinal);
        var prompts = new List<McpPromptEntry>();
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

            if (client.ServerCapabilities?.Resources is not null)
            {
                resources[definition.Name] = new McpResourceClients.Entry(client, definition.ToolTimeoutSeconds);
            }

            foreach (var prompt in load.Prompts.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (!McpServerNames.TryPromptCommand(definition.Name, prompt.Name, out var command))
                {
                    notes.Add(
                        $"MCP prompt '{prompt.Name}' from '{definition.Name}' was skipped: the name is too long or empty.");
                    continue;
                }

                if (prompts.Any(item => item.Command == command))
                {
                    notes.Add(
                        $"MCP prompt '{prompt.Name}' from '{definition.Name}' was skipped: "
                        + $"the command '/{command}' is already in use.");
                    continue;
                }

                prompts.Add(new McpPromptEntry(
                    command,
                    definition.Name,
                    prompt.Name,
                    prompt.Description ?? prompt.Title ?? string.Empty,
                    [.. (prompt.Arguments ?? []).Select(item => new McpPromptArgument(item.Name, item.Required == true))],
                    client,
                    definition.ToolTimeoutSeconds));
            }

            servers.Add(new McpServerInfo(
                definition.Name,
                definition.Source,
                definition.Transport,
                McpConnectionStatus.Connected,
                count,
                string.Empty));
        }

        return new McpCatalog(
            kept, plan, work, servers, infos, notes, new McpResourceClients(resources), prompts);
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
        string Error,
        IReadOnlyList<Prompt>? PromptList = null)
    {
        public IReadOnlyList<Prompt> Prompts => PromptList ?? [];
    }
}
