using System.IO.Pipelines;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrystalCode.Engine.Tests.Mcp;

/// <summary>
/// An MCP server that runs inside the test process and talks to a real
/// <see cref="McpClient"/> over in-memory pipes.
/// </summary>
internal sealed class InProcessMcpServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly McpServer _server;
    private readonly Task _running;

    private InProcessMcpServer(
        McpServer server,
        Task running,
        CancellationTokenSource stop,
        McpClient client)
    {
        _server = server;
        _running = running;
        _stop = stop;
        Client = client;
    }

    public McpClient Client { get; }

    public static async Task<InProcessMcpServer> StartAsync(params McpServerTool[] tools)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var toolCollection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in tools)
        {
            toolCollection.Add(tool);
        }

        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream()),
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
                ToolCollection = toolCollection
            });
        var stop = new CancellationTokenSource();
        // The server must be reading before the client sends initialize.
        var running = server.RunAsync(stop.Token);
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()));
        return new InProcessMcpServer(server, running, stop, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _stop.CancelAsync();
        try
        {
            await _running;
        }
        catch (OperationCanceledException)
        {
        }

        await _server.DisposeAsync();
        _stop.Dispose();
    }
}
