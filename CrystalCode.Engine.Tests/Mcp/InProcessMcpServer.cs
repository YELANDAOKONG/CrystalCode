using System.IO.Pipelines;

using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrystalCode.Engine.Tests.Mcp;

/// <summary>
/// An MCP server that runs inside the test process and talks over in-memory
/// pipes. <see cref="Client"/> is a plain SDK client; <see cref="Connect"/>
/// connects through the engine's own handlers so roots, elicitation, and
/// change notifications behave as in a session.
/// </summary>
internal sealed class InProcessMcpServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _running;
    private readonly List<McpClient> _owned = [];

    private InProcessMcpServer(
        McpServer server,
        Task running,
        CancellationTokenSource stop,
        McpClient client)
    {
        Server = server;
        _running = running;
        _stop = stop;
        Client = client;
    }

    public McpServer Server { get; }

    public McpClient Client { get; }

    public static Task<InProcessMcpServer> StartAsync(params McpServerTool[] tools) =>
        StartAsync(new Features { Tools = tools });

    /// <summary>
    /// Starts a server and connects the engine's own client options to it, so
    /// the declared capabilities and handlers are the ones a session uses.
    /// </summary>
    public static Task<InProcessMcpServer> StartWithHostAsync(
        Features features,
        McpServerDefinition definition,
        McpHostCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(features);
        return StartAsync(new Features
        {
            Tools = features.Tools,
            Prompts = features.Prompts,
            Resources = features.Resources,
            ClientOptions = Initialize(McpConnection.CreateOptions(definition, callbacks))
        });
    }

    public static async Task<InProcessMcpServer> StartAsync(Features features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "test-server", Version = "1.0.0" },
            ToolCollection = Collect(features.Tools),
            PromptCollection = Collect(features.Prompts)
        };
        foreach (var resource in features.Resources)
        {
            options.ResourceCollection ??= [];
            options.ResourceCollection.Add(resource);
        }

        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream()),
            options);
        var stop = new CancellationTokenSource();
        // The server must be reading before the client sends initialize.
        var running = server.RunAsync(stop.Token);
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            features.ClientOptions);
        return new InProcessMcpServer(server, running, stop, client);
    }

    // Pin the initialize handshake: the server then learns the client's
    // capabilities once, at connect time, the way stdio servers do today.
    private static McpClientOptions Initialize(McpClientOptions options)
    {
        options.ProtocolVersion = "2025-11-25";
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var owned in _owned)
        {
            await owned.DisposeAsync();
        }

        await Client.DisposeAsync();
        await _stop.CancelAsync();
        try
        {
            await _running;
        }
        catch (OperationCanceledException)
        {
        }

        await Server.DisposeAsync();
        _stop.Dispose();
    }

    private static McpServerPrimitiveCollection<T> Collect<T>(IEnumerable<T> items)
        where T : IMcpServerPrimitive
    {
        var collection = new McpServerPrimitiveCollection<T>();
        foreach (var item in items)
        {
            collection.Add(item);
        }

        return collection;
    }

    public sealed class Features
    {
        public IReadOnlyList<McpServerTool> Tools { get; init; } = [];

        public IReadOnlyList<McpServerPrompt> Prompts { get; init; } = [];

        public IReadOnlyList<McpServerResource> Resources { get; init; } = [];

        public McpClientOptions? ClientOptions { get; init; }
    }
}
