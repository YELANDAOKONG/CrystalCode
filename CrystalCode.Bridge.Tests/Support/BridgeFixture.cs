using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

using CrystalCode.Bridge.Hosting;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Bridge.Tests.Support;

/// <summary>
/// Bridge on a pair of in-memory pipes, played by a test ACP client, with a
/// temporary home and workspace and a scripted model. Nothing here touches
/// the operator's ~/.crystal. Client requests from Bridge, such as
/// <c>session/request_permission</c>, go to <see cref="OnRequest"/>.
/// </summary>
/// <remarks>
/// Anonymous OS pipes are synchronous on Windows, so each pending read holds a
/// thread-pool thread; parallel fixtures starve the pool and time out.
/// In-memory pipes keep every read and write truly asynchronous.
/// </remarks>
internal sealed class BridgeFixture : IAsyncDisposable
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    // Writers never pause, so a slow reader cannot block a synchronous write.
    private static readonly PipeOptions Unbounded = new(pauseWriterThreshold: 0, resumeWriterThreshold: 0);

    private readonly Stream _toBridge;
    private readonly Stream _fromBridge;
    private readonly Stream _bridgeInput;
    private readonly Stream _bridgeOutput;
    private readonly CancellationTokenSource _stop = new();
    private readonly StringWriter _error = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Channel<JsonElement> _messages = Channel.CreateUnbounded<JsonElement>();
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _responses = [];
    private readonly List<JsonElement> _seen = [];
    private Task _run = Task.CompletedTask;
    private Task _pump = Task.CompletedTask;
    private long _nextId;

    private BridgeFixture(string root)
    {
        Root = root;
        Home = Path.Combine(root, "home");
        Workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Workspace);
        var input = new Pipe(Unbounded);
        var output = new Pipe(Unbounded);
        _toBridge = input.Writer.AsStream();
        _bridgeInput = input.Reader.AsStream();
        _bridgeOutput = output.Writer.AsStream();
        _fromBridge = output.Reader.AsStream();
    }

    public string Root { get; }

    public string Home { get; }

    public string Workspace { get; }

    public string Error => _error.ToString();

    /// <summary>Answers a request Bridge sends; returns the result, or null for an error response.</summary>
    public Func<string, JsonElement, JsonNode?> OnRequest { get; set; } = static (_, _) => null;

    /// <summary>Every raw stdout line, in order.</summary>
    public List<string> Lines { get; } = [];

    public Task Run => _run;

    public static async Task<BridgeFixture> StartAsync(
        ScriptedModel model,
        bool trusted = true,
        Func<SessionLaunchOptions, SessionLaunchOptions>? options = null,
        Action<CrystalHome, string>? prepare = null,
        IReadOnlyList<string>? configOptions = null,
        TextWriter? debug = null)
    {
        var fixture = new BridgeFixture(Directory.CreateTempSubdirectory("crystal-bridge-").FullName);
        var home = new CrystalHome(fixture.Home);
        new DiskSettingsStore(home).LoadOrCreate();
        if (trusted)
        {
            new WorkspaceTrustStore(home).Remember(fixture.Workspace);
        }

        prepare?.Invoke(home, fixture.Workspace);
        var launch = new SessionLaunchOptions
        {
            Home = fixture.Home,
            Provider = "ollama",
            Model = "qwen3:8b",
            Skills = "off",
            ExternalTools = "off",
            Plugins = "off",
            Mcp = "off"
        };
        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(model);
        var bridgeOptions = new BridgeOptions(
            options?.Invoke(launch) ?? launch,
            fixture.Workspace,
            plugins,
            "test",
            fixture._error,
            ConfigOptions: configOptions,
            Debug: debug);
        fixture._run = Task.Run(() => CrystalBridge.RunAsync(
            bridgeOptions,
            fixture._bridgeInput,
            fixture._bridgeOutput,
            fixture._stop.Token));
        fixture._pump = Task.Run(fixture.PumpAsync);
        await Task.Yield();
        return fixture;
    }

    /// <summary>initialize followed by session/new in the workspace; returns the session id.</summary>
    public async Task<string> OpenAsync(object? clientCapabilities = null)
    {
        await RequestAsync("initialize", new { protocolVersion = 1, clientCapabilities = clientCapabilities ?? new { } });
        var created = await RequestAsync("session/new", new { cwd = Workspace, mcpServers = Array.Empty<object>() });
        return created.GetProperty("result").GetProperty("sessionId").GetString()!;
    }

    /// <summary>Sends a request and returns the full response object.</summary>
    public async Task<JsonElement> RequestAsync(string method, object parameters)
    {
        var (_, response) = await StartRequestAsync(method, parameters);
        return await response.WaitAsync(Wait);
    }

    public async Task<(long Id, Task<JsonElement> Response)> StartRequestAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_responses)
        {
            _responses[id] = completion;
        }

        await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters });
        return (id, completion.Task);
    }

    public Task NotifyAsync(string method, object parameters) =>
        WriteAsync(new { jsonrpc = "2.0", method, @params = parameters });

    public async Task WriteLineAsync(string line)
    {
        await _writeGate.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await _toBridge.WriteAsync(bytes);
            await _toBridge.FlushAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Closes stdin, as an editor does when it stops the Agent.</summary>
    public async Task CloseInputAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            _toBridge.Dispose();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Session updates seen so far, as their <c>update</c> objects.</summary>
    public IReadOnlyList<JsonElement> Updates()
    {
        lock (_seen)
        {
            return [.. _seen
                .Where(static message => message.TryGetProperty("method", out var method)
                    && method.GetString() == "session/update")
                .Select(static message => message.GetProperty("params").GetProperty("update"))];
        }
    }

    /// <summary>Waits for a notification or request from Bridge that matches.</summary>
    public async Task<JsonElement> WaitForAsync(Func<JsonElement, bool> match)
    {
        using var timeout = new CancellationTokenSource(Wait);
        lock (_seen)
        {
            foreach (var seen in _seen)
            {
                if (match(seen))
                {
                    return seen;
                }
            }
        }

        while (await _messages.Reader.WaitToReadAsync(timeout.Token))
        {
            while (_messages.Reader.TryRead(out var value))
            {
                if (match(value))
                {
                    return value;
                }
            }
        }

        throw new InvalidOperationException("Bridge closed its output first.");
    }

    public Task<JsonElement> WaitForUpdateAsync(string kind, Func<JsonElement, bool>? match = null) =>
        WaitForAsync(message => message.TryGetProperty("method", out var method)
            && method.GetString() == "session/update"
            && message.GetProperty("params").GetProperty("update").GetProperty("sessionUpdate").GetString() == kind
            && (match is null || match(message.GetProperty("params").GetProperty("update"))));

    private async Task WriteAsync(object message) =>
        await WriteLineAsync(JsonSerializer.Serialize(message));

    private async Task PumpAsync()
    {
        try
        {
            using var reader = new StreamReader(_fromBridge, Encoding.UTF8);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                lock (Lines)
                {
                    Lines.Add(line);
                }

                using var document = JsonDocument.Parse(line);
                var value = document.RootElement.Clone();
                if (!value.TryGetProperty("method", out var method))
                {
                    TaskCompletionSource<JsonElement>? completion = null;
                    if (value.GetProperty("id") is { ValueKind: JsonValueKind.Number } idElement
                        && idElement.TryGetInt64(out var id))
                    {
                        lock (_responses)
                        {
                            _responses.Remove(id, out completion);
                        }
                    }

                    completion?.TrySetResult(value);
                    if (completion is null)
                    {
                        // A response with no request of ours, such as a parse error.
                        lock (_seen)
                        {
                            _seen.Add(value);
                        }

                        _messages.Writer.TryWrite(value);
                    }
                }
                else if (value.TryGetProperty("id", out var requestId))
                {
                    lock (_seen)
                    {
                        _seen.Add(value);
                    }

                    _messages.Writer.TryWrite(value);
                    var result = OnRequest(method.GetString()!, value.GetProperty("params"));
                    if (result is not null)
                    {
                        await WriteAsync(new { jsonrpc = "2.0", id = requestId.GetInt64(), result });
                    }
                }
                else
                {
                    lock (_seen)
                    {
                        _seen.Add(value);
                    }

                    _messages.Writer.TryWrite(value);
                }
            }
        }
        // A completed pipe writer throws InvalidOperationException when teardown races an answer.
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            _messages.Writer.TryComplete();
        }
    }

    /// <summary>Answers a pending Bridge request by id.</summary>
    public Task AnswerAsync(long id, object result) =>
        WriteAsync(new { jsonrpc = "2.0", id, result });

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _run.WaitAsync(Wait);
        }
        catch (TimeoutException)
        {
        }

        _toBridge.Dispose();
        _bridgeInput.Dispose();
        _bridgeOutput.Dispose();
        try
        {
            await _pump.WaitAsync(Wait);
        }
        catch (TimeoutException)
        {
        }

        _fromBridge.Dispose();
        _stop.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
