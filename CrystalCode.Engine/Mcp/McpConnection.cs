using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One MCP connection Crystal maintains for a session. Connecting starts
/// when the instance is created; the connection stays open until it is
/// disposed.
/// </summary>
internal sealed class McpConnection : IAsyncDisposable
{
    private const int InitializationTimeoutSeconds = 30;
    private const int DiscoverProbeMilliseconds = 500;

    private readonly CancellationTokenSource _lifecycle;
    private readonly Task<McpConnectionResult> _ready;
    private int _disposed;

    private McpConnection(
        McpServerDefinition definition,
        CancellationTokenSource lifecycle,
        Task<McpConnectionResult> ready)
    {
        Definition = definition;
        _lifecycle = lifecycle;
        _ready = ready;
    }

    public McpServerDefinition Definition { get; }

    public McpConnectionStatus Status
    {
        get
        {
            if (!_ready.IsCompleted)
            {
                return McpConnectionStatus.Connecting;
            }

            return _ready.IsCompletedSuccessfully && _ready.Result.Client is not null
                ? McpConnectionStatus.Connected
                : McpConnectionStatus.Failed;
        }
    }

    /// <summary>
    /// Starts connecting and returns at once. A stdio server runs in
    /// <paramref name="workspaceRoot"/>.
    /// </summary>
    public static McpConnection Connect(McpServerDefinition definition, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var lifecycle = new CancellationTokenSource();
        var ready = OpenAsync(definition, workspaceRoot, lifecycle.Token);
        return new McpConnection(definition, lifecycle, ready);
    }

    /// <summary>
    /// Wraps a client that is already connected, so tests can use an
    /// in-process server instead of a child process or a network endpoint.
    /// </summary>
    internal static McpConnection FromClient(McpServerDefinition definition, McpClient client)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(client);
        return new McpConnection(
            definition,
            new CancellationTokenSource(),
            Task.FromResult(McpConnectionResult.Success(client)));
    }

    /// <summary>
    /// Waits until the connection succeeds or fails. It can be called more
    /// than once and always returns the same result.
    /// </summary>
    public Task<McpConnectionResult> WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        _ready.WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifecycle.CancelAsync();
        try
        {
            var result = await _ready;
            if (result.Client is not null)
            {
                await result.Client.DisposeAsync();
            }
        }
        finally
        {
            _lifecycle.Dispose();
        }
    }

    private static async Task<McpConnectionResult> OpenAsync(
        McpServerDefinition definition,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var tail = new McpErrorTail();
        IClientTransport transport;
        try
        {
            transport = CreateTransport(definition, workspaceRoot, tail);
        }
        catch (InvalidOperationException exception)
        {
            return McpConnectionResult.Failure(
                $"MCP server '{definition.Name}' was not started: {exception.Message}");
        }

        var options = new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "CrystalCode",
                Version = typeof(McpConnection).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            },
            InitializationTimeout = TimeSpan.FromSeconds(InitializationTimeoutSeconds),
            // Servers that predate the newest protocol revision drop the
            // server/discover probe without answering. The probe wait is the
            // delay before the initialize fallback, so keep it short.
            DiscoverProbeTimeout = TimeSpan.FromMilliseconds(DiscoverProbeMilliseconds)
        };

        try
        {
            var client = await McpClient.CreateAsync(
                transport,
                options,
                cancellationToken: cancellationToken);
            return McpConnectionResult.Success(client);
        }
        catch (OperationCanceledException)
        {
            return McpConnectionResult.Failure($"MCP server '{definition.Name}' was closed while connecting.");
        }
        catch (Exception exception) when (exception is McpException
                                              or IOException
                                              or HttpRequestException
                                              or InvalidOperationException
                                              or TimeoutException)
        {
            var output = tail.Text();
            return McpConnectionResult.Failure(
                $"MCP server '{definition.Name}' could not connect: {exception.Message}"
                + (output.Length == 0 ? string.Empty : $" Server output: {output}"));
        }
    }

    private static IClientTransport CreateTransport(
        McpServerDefinition definition,
        string workspaceRoot,
        McpErrorTail tail)
    {
        Func<string, string?> lookup = Environment.GetEnvironmentVariable;
        if (definition.Transport == McpTransportKind.Stdio)
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in definition.Environment)
            {
                environment[pair.Key] = Expand(pair.Value, lookup, $"environment variable '{pair.Key}'");
            }

            return new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Command = Expand(definition.Command!, lookup, "command"),
                    Arguments = [.. definition.Arguments.Select(
                        argument => Expand(argument, lookup, "argument"))],
                    Name = definition.Name,
                    WorkingDirectory = workspaceRoot,
                    EnvironmentVariables = environment,
                    StandardErrorLines = tail.Add
                });
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in definition.Headers)
        {
            headers[pair.Key] = Expand(pair.Value, lookup, $"header '{pair.Key}'");
        }

        if (!Uri.TryCreate(Expand(definition.Url!, lookup, "URL"), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("the URL must be an absolute http or https address.");
        }

        return new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = definition.Transport == McpTransportKind.Sse
                    ? HttpTransportMode.Sse
                    : HttpTransportMode.StreamableHttp,
                Name = definition.Name,
                AdditionalHeaders = headers,
                ConnectionTimeout = TimeSpan.FromSeconds(InitializationTimeoutSeconds)
            });
    }

    private static string Expand(string text, Func<string, string?> lookup, string label)
    {
        if (McpEnvironmentText.TryExpand(text, lookup, out var expanded, out var missing))
        {
            return expanded;
        }

        // Only the variable name is reported; the value may be a secret.
        throw new InvalidOperationException(
            $"the {label} uses ${{{missing}}}, which is not set in the environment.");
    }
}
