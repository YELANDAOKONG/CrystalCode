using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Endpoints;
using CrystalCode.Server.Security;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// The <c>crystal server</c> host: Kestrel, the access guard, the endpoints,
/// and the one live session. <see cref="RunAsync"/> returns the process exit
/// code after the session closed and the listener stopped.
/// </summary>
public sealed class CrystalServer : IAsyncDisposable
{
    public const string TlsPasswordVariable = "CRYSTAL_SERVER_TLS_PASSWORD";

    private readonly ServerOptions _options;
    private readonly ServerRuntime _runtime;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ListenAddress _listen;
    private WebApplication? _app;
    private ServerDiscovery? _discovery;

    private CrystalServer(ServerOptions options, ServerRuntime runtime, ListenAddress listen)
    {
        _options = options;
        _runtime = runtime;
        _listen = listen;
        Session = new ServerSession(options, runtime);
    }

    public ServerSession Session { get; }

    /// <summary>The bound URL, available after <see cref="StartAsync"/>.</summary>
    public string? Url { get; private set; }

    public static bool TryCreate(ServerOptions options, ServerRuntime runtime, out CrystalServer server, out string error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);
        server = null!;
        if (!ListenAddress.TryParse(options.Listen, out var listen, out error))
        {
            return false;
        }

        if ((options.TlsCertificate is null) != (options.TlsKey is null))
        {
            error = "Pass both --tls-cert and --tls-key.";
            return false;
        }

        if (options.Socket is not null && options.TlsCertificate is not null)
        {
            error = "--socket cannot be combined with TLS.";
            return false;
        }

        server = new CrystalServer(options, runtime, listen);
        return true;
    }

    /// <summary>
    /// Stops the session and the listener. The first reason is announced to
    /// clients at once and reported on the operator's terminal.
    /// </summary>
    public void Shutdown(string reason)
    {
        Session.RequestStop(reason);
        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Binds the listener, starts the session, and writes the listening line.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = Engine.Sessions.ImageFile.MaximumBytes + 1024;
            if (_options.Socket is { } socket)
            {
                if (File.Exists(socket))
                {
                    File.Delete(socket);
                }

                kestrel.ListenUnixSocket(socket);
                return;
            }

            var address = _listen.Localhost ? IPAddress.Loopback : _listen.Address;
            kestrel.Listen(address, _listen.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                if (_options.TlsCertificate is { } certificate)
                {
                    // A key password is never a flag; it comes from the environment.
                    var password = Environment.GetEnvironmentVariable(TlsPasswordVariable);
                    listen.UseHttps(string.IsNullOrEmpty(password)
                        ? X509Certificate2.CreateFromPemFile(certificate, _options.TlsKey)
                        : X509Certificate2.CreateFromEncryptedPemFile(certificate, password, _options.TlsKey));
                }
            });
        });
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        var tokens = _options.Token is { } token ? new TokenStore(token) : null;
        var clients = new ClientRegistry();
        var guard = new AccessGuard(tokens, _options.AllowedHosts, _options.AllowedOrigins);
        if (!_listen.IsLoopback && !_listen.IsAny)
        {
            guard.AllowHost(_listen.Address.ToString());
        }

        clients.Disconnected += Session.Broker.Disconnected;
        Session.Broker.BindConnections(clients.IsConnected);
        var context = new ServerContext(
            _options,
            _runtime,
            Session,
            tokens,
            clients,
            new RequestLedger(),
            () => Shutdown(ServerStopReason.Request));
        app.Use(guard.InvokeAsync);
        ServerEndpoints.Map(app, context);
        await app.StartAsync(cancellationToken);
        _app = app;

        Url = _options.Socket is { } path
            ? "unix:" + path
            : app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        if (_options.Socket is { } created && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(created, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await WarnAsync();
        var workspace = Path.GetFullPath(_options.Launch.Workspace ?? _options.Launch.WorkingDirectory);
        var home = CrystalHome.Resolve(_options.Launch.Options.Home);
        if (_options.WriteDiscovery)
        {
            _discovery = ServerDiscovery.Write(
                home,
                Url,
                workspace,
                _options.TokenGenerated ? _options.Token : null,
                writer => ServerEndpoints.WriteBuild(writer, _runtime.Build));
        }

        await _runtime.Output.WriteLineAsync(ListeningLine(workspace));
        await _runtime.Output.FlushAsync(cancellationToken);
        await WriteSummaryAsync(workspace);
        Session.StateChanged += ReportState;
    }

    /// <summary>Runs until the session quits or shutdown is requested. Returns the exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        if (_app is null)
        {
            await StartAsync(linked.Token);
        }

        await Session.RunAsync(linked.Token);
        Session.Hub.CompleteAll();
        var closure = Session.Closed.IsCompletedSuccessfully ? await Session.Closed : null;
        var code = Session.Failure is null ? 0 : 1;
        if (Session.Failure is { } failure)
        {
            await _runtime.Error.WriteLineAsync(failure);
        }
        else
        {
            if (closure is { Saved: true })
            {
                await _runtime.Error.WriteLineAsync("Session saved  " + closure.SessionId);
            }

            await _runtime.Error.WriteLineAsync("Server stopped");
        }

        // The last stdout line, so a wrapper that read "listening" also sees the end.
        await _runtime.Output.WriteLineAsync(StoppedLine(closure, code));
        await _runtime.Output.FlushAsync(CancellationToken.None);
        return code;
    }

    public async ValueTask DisposeAsync()
    {
        _discovery?.Delete();
        Session.Hub.CompleteAll();
        if (_app is not null)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _app.StopAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
            }

            await _app.DisposeAsync();
        }

        if (_options.Socket is { } socket && File.Exists(socket))
        {
            File.Delete(socket);
        }

        _shutdown.Dispose();
    }

    private async Task WarnAsync()
    {
        if (!_options.AuthenticationEnabled)
        {
            await _runtime.Error.WriteLineAsync(
                "Warning: authentication is off. Anyone who can reach this address can run commands in this workspace.");
        }

        if (_options.Socket is null && !_listen.IsLoopback && _options.TlsCertificate is null)
        {
            await _runtime.Error.WriteLineAsync(
                "Warning: this listener is reachable from the network without TLS. Tokens and session content travel in clear text.");
        }

    }

    /// <summary>
    /// The operator-facing startup summary on stderr. stdout stays one JSON
    /// line for wrappers. A generated token is shown only on a terminal.
    /// </summary>
    private async Task WriteSummaryAsync(string workspace)
    {
        var rows = new List<(string Label, string Value)> { ("Server", Url!) };
        if (_options.Socket is null)
        {
            var bound = new Uri(Url!);
            rows.Add(("Listen", bound.Host.Trim('[', ']') + (_listen.IsAny ? " (every interface)" : string.Empty)));
            rows.Add(("Port", bound.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        rows.Add(("Workspace", workspace));
        rows.Add(("Auth", _options.AuthenticationEnabled ? "token" : "none"));
        if (_options.TokenGenerated && _options.Token is { } token && _runtime.ShowGeneratedToken)
        {
            rows.Add(("Token", token));
        }
        else if (_options.AuthenticationEnabled)
        {
            rows.Add(("Token", _options.TokenGenerated ? "in the discovery file" : "supplied at launch"));
        }

        if (_discovery is not null)
        {
            rows.Add(("Discovery", _discovery.Path));
        }

        rows.Add(("PID", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var width = rows.Max(static row => row.Label.Length);
        foreach (var (label, value) in rows)
        {
            await _runtime.Error.WriteLineAsync(label.PadRight(width) + "  " + value);
        }

        await _runtime.Error.WriteLineAsync("Ctrl+C stops the server.");
        await _runtime.Error.FlushAsync();
    }

    /// <summary>Tells the operator when the server waits on a client or starts to stop.</summary>
    private void ReportState(string state)
    {
        var line = state switch
        {
            ServerState.AwaitingOperator => "Waiting for a client to answer a startup question",
            ServerState.Ready => "Session ready",
            ServerState.Closing => "Stopping  " + ServerStopReason.Describe(Session.StopReason ?? ServerStopReason.Cancelled),
            _ => null
        };
        if (line is null)
        {
            return;
        }

        lock (_runtime.Error)
        {
            _runtime.Error.WriteLine(line);
            _runtime.Error.Flush();
        }
    }

    private string StoppedLine(SessionClosure? closure, int code)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("event", "stopped");
            writer.WriteNumber("pid", Environment.ProcessId);
            writer.WriteNumber("exitCode", code);
            WireWriter.Optional(writer, "reason", Session.Failure is null ? Session.StopReason : ServerState.Failed);
            WireWriter.Optional(writer, "sessionId", closure?.SessionId);
            writer.WriteBoolean("saved", closure?.Saved ?? false);
            if (Session.Failure is { } failure)
            {
                writer.WriteString("error", failure);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private string ListeningLine(string workspace)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("event", "listening");
            writer.WriteString("url", Url);
            writer.WriteNumber("pid", Environment.ProcessId);
            writer.WriteString("workspace", workspace);
            if (_discovery is not null)
            {
                writer.WriteString("discovery", _discovery.Path);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
