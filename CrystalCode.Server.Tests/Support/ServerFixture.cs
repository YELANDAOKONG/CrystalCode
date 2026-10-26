using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Version;
using CrystalCode.Server.Hosting;

namespace CrystalCode.Server.Tests.Support;

/// <summary>
/// A real <see cref="CrystalServer"/> on a loopback port chosen by the OS,
/// with a temporary home and workspace and a scripted model. Nothing here
/// touches the operator's ~/.crystal.
/// </summary>
internal sealed class ServerFixture : IAsyncDisposable
{
    public const string Token = "fixture-token-0123456789";

    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _stop = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private Task<int>? _run;

    private ServerFixture(string root)
    {
        Root = root;
        Home = Path.Combine(root, "home");
        Workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Workspace);
    }

    public string Root { get; }

    public string Home { get; }

    public string Workspace { get; }

    public CrystalServer Server { get; private set; } = null!;

    public HttpClient Http { get; private set; } = null!;

    public string Error => _error.ToString();

    public string Output => _output.ToString();

    public static async Task<ServerFixture> StartAsync(
        ScriptedModel model,
        bool trusted = true,
        bool authentication = true,
        Func<SessionLaunchOptions, SessionLaunchOptions>? options = null,
        Action<CrystalHome, string>? prepare = null,
        IReadOnlyList<string>? origins = null)
    {
        var fixture = new ServerFixture(Directory.CreateTempSubdirectory("crystal-server-").FullName);
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
        var serverOptions = new ServerOptions
        {
            Launch = new SessionLaunchRequest(options?.Invoke(launch) ?? launch, fixture.Workspace, fixture.Workspace),
            Token = authentication ? Token : null,
            AllowedOrigins = origins ?? [],
            Heartbeat = TimeSpan.FromSeconds(1),
            WriteDiscovery = true
        };
        var runtime = new ServerRuntime(
            fixture._output,
            fixture._error,
            plugins,
            new BuildIdentity(null, null, null, null, null, null));
        if (!CrystalServer.TryCreate(serverOptions, runtime, out var server, out var error))
        {
            throw new InvalidOperationException(error);
        }

        fixture.Server = server;
        await server.StartAsync(fixture._stop.Token);
        fixture._run = server.RunAsync(fixture._stop.Token);
        fixture.Http = fixture.Client("test-client");
        return fixture;
    }

    public HttpClient Client(string client, string? token = Token)
    {
        var http = new HttpClient { BaseAddress = new Uri(Server.Url!), Timeout = Wait };
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        http.DefaultRequestHeaders.Add("X-Crystal-Client", client);
        return http;
    }

    public async Task WaitReadyAsync() => await Server.Session.Ready.WaitAsync(Wait);

    public async Task<int> StopAsync()
    {
        await _stop.CancelAsync();
        return await _run!.WaitAsync(Wait);
    }

    public Task<int> ExitAsync() => _run!.WaitAsync(Wait);

    public static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {text}");
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> PostAsync(HttpClient http, string path, object body) =>
        await ReadJsonAsync(await http.PostAsync(path, Body(body)));

    public async Task<JsonElement> GetAsync(HttpClient http, string path) =>
        await ReadJsonAsync(await http.GetAsync(path));

    /// <summary>Opens <c>/events</c> and pushes each parsed event onto a channel.</summary>
    public async Task<EventReader> OpenEventsAsync(HttpClient http, long after = 0)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/events?after=" + after);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return new EventReader(response, await response.Content.ReadAsStreamAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (_run is not null && !_run.IsCompleted)
        {
            await _stop.CancelAsync();
            try
            {
                await _run.WaitAsync(Wait);
            }
            catch (TimeoutException)
            {
            }
        }

        Http?.Dispose();
        if (Server is not null)
        {
            await Server.DisposeAsync();
        }

        _stop.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reads SSE messages in the background.</summary>
    internal sealed class EventReader : IDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();
        private readonly CancellationTokenSource _stop = new();

        public EventReader(HttpResponseMessage response, Stream stream)
        {
            _response = response;
            _ = Task.Run(() => PumpAsync(stream));
        }

        public List<JsonElement> Seen { get; } = [];

        public bool Resync { get; private set; }

        /// <summary>Waits for the first event of <paramref name="type"/> matching the condition.</summary>
        public async Task<JsonElement> WaitForAsync(string type, Func<JsonElement, bool>? match = null)
        {
            using var timeout = new CancellationTokenSource(Wait);
            lock (Seen)
            {
                foreach (var seen in Seen)
                {
                    if (Matches(seen, type, match))
                    {
                        return seen;
                    }
                }
            }

            while (await _events.Reader.WaitToReadAsync(timeout.Token))
            {
                while (_events.Reader.TryRead(out var value))
                {
                    if (Matches(value, type, match))
                    {
                        return value;
                    }
                }
            }

            throw new InvalidOperationException("The stream ended before " + type + ".");
        }

        public IReadOnlyList<string> Types()
        {
            lock (Seen)
            {
                return [.. Seen.Select(static value => value.GetProperty("type").GetString()!)];
            }
        }

        private static bool Matches(JsonElement value, string type, Func<JsonElement, bool>? match) =>
            value.GetProperty("type").GetString() == type && (match is null || match(value));

        private async Task PumpAsync(Stream stream)
        {
            try
            {
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = await reader.ReadLineAsync(_stop.Token)) is not null)
                {
                    if (line.StartsWith("event: resync", StringComparison.Ordinal))
                    {
                        Resync = true;
                    }

                    if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(line["data: ".Length..]);
                    var value = document.RootElement.Clone();
                    lock (Seen)
                    {
                        Seen.Add(value);
                    }

                    _events.Writer.TryWrite(value);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or HttpRequestException)
            {
            }
            finally
            {
                _events.Writer.TryComplete();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _response.Dispose();
            _stop.Dispose();
        }
    }
}
