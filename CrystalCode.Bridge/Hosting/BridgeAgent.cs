using System.Text.Json;

using CrystalCode.Bridge.Prompts;
using CrystalCode.Bridge.Transport;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// The ACP Agent side of one connection. It answers <c>initialize</c>,
/// <c>session/new</c>, <c>session/prompt</c>, <c>session/cancel</c>, and
/// <c>session/close</c>, and holds at most one live session. Opening,
/// replacing, and closing that session are serialized; prompts and cancels
/// run beside them.
/// </summary>
internal sealed class BridgeAgent : IJsonRpcHandler
{
    public const int ProtocolVersion = 1;

    private readonly BridgeOptions _options;
    private readonly JsonRpcConnection _connection;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private ClientFeatures _features = ClientFeatures.None;
    private BridgeSession? _active;
    private long _prompts;

    public BridgeAgent(BridgeOptions options, JsonRpcConnection connection)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connection);
        _options = options;
        _connection = connection;
    }

    public async Task<JsonRpcReply> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellationToken) =>
        method switch
        {
            "initialize" => Initialize(parameters),
            "session/new" => await NewAsync(parameters, cancellationToken),
            "session/prompt" => await PromptAsync(parameters, cancellationToken),
            "session/close" => await CloseAsync(parameters),
            _ => throw new JsonRpcException(JsonRpcErrorCode.MethodNotFound, "Method not found: " + PromptContent.OneLine(method))
        };

    public Task HandleNotificationAsync(string method, JsonElement parameters)
    {
        if (method == "session/cancel"
            && JsonParams.OptionalString(parameters, "sessionId") is { } id
            && Volatile.Read(ref _active) is { } active
            && active.Id == id)
        {
            active.Cancel();
        }

        // Other notifications, including unknown extensions, are ignored.
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await CloseActiveAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private JsonRpcReply Initialize(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("protocolVersion", out var version)
            || !version.TryGetInt32(out _))
        {
            throw JsonRpcException.InvalidParams("protocolVersion must be an integer.");
        }

        _features = ClientFeatures.Read(parameters);
        var agentVersion = _options.Version;
        return new JsonRpcReply(writer =>
        {
            // Every client version gets v1, the only version implemented.
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", ProtocolVersion);
            writer.WriteStartObject("agentInfo");
            writer.WriteString("name", "crystal-code");
            writer.WriteString("title", "Crystal Code");
            writer.WriteString("version", agentVersion);
            writer.WriteEndObject();
            writer.WriteStartArray("authMethods");
            writer.WriteEndArray();
            writer.WriteStartObject("agentCapabilities");
            writer.WriteBoolean("loadSession", false);
            writer.WriteStartObject("promptCapabilities");
            writer.WriteBoolean("image", true);
            writer.WriteBoolean("audio", false);
            writer.WriteBoolean("embeddedContext", true);
            writer.WriteEndObject();
            writer.WriteStartObject("mcpCapabilities");
            writer.WriteBoolean("http", false);
            writer.WriteBoolean("sse", false);
            writer.WriteEndObject();
            writer.WriteStartObject("sessionCapabilities");
            writer.WriteStartObject("close");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartObject("_meta");
            writer.WriteStartObject("crystal");
            writer.WriteStartArray("extensions");
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private async Task<JsonRpcReply> NewAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var cwd = JsonParams.OptionalString(parameters, "cwd");
        if (cwd is not null && !Path.IsPathFullyQualified(cwd))
        {
            throw JsonRpcException.InvalidParams("cwd must be an absolute path.");
        }

        if (JsonParams.OptionalArray(parameters, "additionalDirectories") is { } additional
            && additional.GetArrayLength() > 0)
        {
            throw JsonRpcException.InvalidParams("additionalDirectories is not supported by this build.");
        }

        if (JsonParams.OptionalArray(parameters, "mcpServers") is { } servers && servers.GetArrayLength() > 0)
        {
            // Names, commands, and headers can carry secrets; only the count is written.
            await _options.Diagnostics.WriteLineAsync(
                "Bridge ignored " + servers.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " MCP servers from the client. Client MCP servers are not supported by this build.");
        }

        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await CloseActiveAsync();
            var bridge = await OpenAsync(cwd, cancellationToken);
            Volatile.Write(ref _active, bridge);
            var id = bridge.Id;
            return new JsonRpcReply(
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("sessionId", id);
                    writer.WriteEndObject();
                },
                bridge.Release);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<BridgeSession> OpenAsync(string? cwd, CancellationToken cancellationToken)
    {
        var request = new SessionLaunchRequest(
            _options.Launch,
            _options.WorkingDirectory,
            cwd ?? _options.Workspace,
            cwd is null && _options.Space,
            Trust: SessionTrustCheck.Prompt,
            PersistSelection: true);
        var prepared = await SessionLauncher.PrepareAsync(request, trust: null, chooser: null, cancellationToken);
        if (prepared.Launch is not { } launch)
        {
            throw prepared.Failure == SessionLaunchFailureKind.TrustNeedsOperator
                ? JsonRpcException.Refused("trust_refused", UntrustedText(cwd))
                : JsonRpcException.Refused(
                    "launch_refused",
                    prepared.Error.Length > 0 ? prepared.Error : "The session could not be opened.");
        }

        foreach (var note in launch.Notes)
        {
            await _options.Diagnostics.WriteLineAsync(note);
        }

        var bridge = new BridgeSession(_connection, _features);
        CodingSession session;
        try
        {
            session = launch.CreateSession(new SessionFrontEnd(bridge, bridge, bridge, bridge, bridge), _options.Plugins);
        }
        catch (Exception exception) when (IsSetupFailure(exception))
        {
            throw JsonRpcException.Refused("launch_refused", exception.Message);
        }

        bridge.Bind(session, launch.Workspace);
        try
        {
            await session.StartAsync(cancellationToken);
            if (launch.Mode is not null && !session.TrySetMode(launch.Mode, out var modeError))
            {
                throw JsonRpcException.Refused("launch_refused", modeError);
            }
        }
        catch (Exception exception) when (IsSetupFailure(exception))
        {
            await session.CloseAsync(CancellationToken.None);
            throw JsonRpcException.Refused("launch_refused", exception.Message);
        }
        catch
        {
            await session.CloseAsync(CancellationToken.None);
            throw;
        }

        bridge.Run(() => _ = CloseIfActiveAsync(bridge));
        return bridge;
    }

    private static bool IsSetupFailure(Exception exception) =>
        exception is (ArgumentException and not ArgumentNullException)
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException;

    private string UntrustedText(string? cwd)
    {
        var directory = Path.GetFullPath(cwd ?? _options.Workspace ?? _options.WorkingDirectory);
        return "The directory " + directory + " is not trusted. Run crystal in that directory once and trust it, "
            + "or set workspaceTrust to false in config.json.";
    }

    private async Task<JsonRpcReply> PromptAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var bridge = Active(parameters);
        if (JsonParams.OptionalArray(parameters, "prompt") is not { } prompt || prompt.GetArrayLength() == 0)
        {
            throw JsonRpcException.InvalidParams("prompt must be a non-empty array of content blocks.");
        }

        var runner = bridge.Runner!;
        // Each prompt is its own origin, so its images stay in its draft and a
        // refusal of its command can be told apart from other errors.
        var origin = "prompt-" + Interlocked.Increment(ref _prompts).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var content = await PromptContent.ReadAsync(prompt, bytes => runner.InvokeAsync(session =>
            session.TryAttachImage(bytes, out var image, out var error)
                ? new ImageAttachResult(image!.TrustedMarker, string.Empty)
                : new ImageAttachResult(null, error), origin));
        if (string.IsNullOrWhiteSpace(content.Text))
        {
            throw JsonRpcException.InvalidParams("prompt has no content.");
        }

        foreach (var warning in content.Warnings)
        {
            bridge.Updates.Warn(warning);
        }

        // A $/cancel_request for this prompt cancels its turn like session/cancel;
        // the request still waits for the turn to end before it answers.
        using var cancel = cancellationToken.Register(bridge.Cancel);
        var command = SessionCommand.TryParse(content.Text.Trim(), out _);
        var clears = bridge.Turns.Clears;
        var before = bridge.Turns.Started;
        var busy = bridge.Session!.TurnActive;
        var target = before + 1;
        using var waiting = command ? null : bridge.Await(target);

        SessionInputResult result;
        try
        {
            result = await runner.SubmitAsync(content.Text, origin);
        }
        catch (InvalidOperationException)
        {
            throw JsonRpcException.NotFound("The session is not active.");
        }

        var refused = bridge.TakeError(origin);
        var startedTurn = bridge.Turns.Started > before;
        var queued = !command && bridge.Queued > 0;
        if (command && refused is not null && !startedTurn)
        {
            throw JsonRpcException.Refused("command_refused", refused);
        }

        if (result.Quit || (command && busy) || (!startedTurn && !queued))
        {
            // A command that started no turn, or /quit: its output already went out.
            cancellationToken.ThrowIfCancellationRequested();
            return Reply(TurnWire.EndTurn, null);
        }

        using var waitingTurn = bridge.Await(target);
        var outcome = await bridge.Turns.Turn(target, clears);
        cancellationToken.ThrowIfCancellationRequested();
        var cancelled = bridge.CancelRequested(target);
        if (outcome.Abandoned)
        {
            return Reply(cancelled ? TurnWire.Cancelled : TurnWire.EndTurn, null);
        }

        if (outcome.Finished is not { } finished)
        {
            return cancelled
                ? Reply(TurnWire.Cancelled, null)
                : throw JsonRpcException.Refused("turn_failed", Failure(outcome.Error));
        }

        var stop = cancelled ? TurnWire.Cancelled : TurnWire.StopReason(finished.Result.StopReason);
        if (stop is null)
        {
            var kind = finished.Result.StopReason == TurnStopReason.ContextOverflow ? "context_overflow" : "turn_failed";
            throw JsonRpcException.Refused(kind, Failure(outcome.Error), writer => TurnWire.Crystal(writer, finished));
        }

        return Reply(stop, finished);
    }

    private static string Failure(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "The turn failed." : error;

    private static JsonRpcReply Reply(string stopReason, Engine.Events.TurnFinished? finished) =>
        new(writer => TurnWire.Response(writer, stopReason, finished));

    private async Task<JsonRpcReply> CloseAsync(JsonElement parameters)
    {
        var bridge = Active(parameters);
        await _lifecycle.WaitAsync();
        try
        {
            if (ReferenceEquals(Volatile.Read(ref _active), bridge))
            {
                await CloseActiveAsync();
            }
        }
        finally
        {
            _lifecycle.Release();
        }

        return JsonRpcReply.Empty;
    }

    /// <summary>The session named by <c>sessionId</c>; any other id is <c>-32002</c>.</summary>
    private BridgeSession Active(JsonElement parameters)
    {
        var id = JsonParams.RequiredString(parameters, "sessionId");
        return Volatile.Read(ref _active) is { } active && active.Id == id
            ? active
            : throw JsonRpcException.NotFound("The session is not active.");
    }

    /// <summary>The runner stopped on its own, after /quit: the session ends.</summary>
    private async Task CloseIfActiveAsync(BridgeSession bridge)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (ReferenceEquals(Volatile.Read(ref _active), bridge))
            {
                await CloseActiveAsync();
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Closes the active session. The caller holds the lifecycle gate.</summary>
    private async Task CloseActiveAsync()
    {
        var active = Interlocked.Exchange(ref _active, null);
        if (active is null)
        {
            return;
        }

        var closure = await active.CloseAsync();
        if (closure is { Saved: true })
        {
            await _options.Diagnostics.WriteLineAsync("Session " + closure.SessionId + " saved.");
        }
    }
}
