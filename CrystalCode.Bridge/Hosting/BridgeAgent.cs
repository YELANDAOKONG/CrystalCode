using System.Globalization;
using System.Text.Json;

using CrystalCode.Bridge.Prompts;
using CrystalCode.Bridge.Transport;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// The ACP Agent side of one connection. It answers <c>initialize</c>, the
/// session lifecycle (<c>new</c>, <c>load</c>, <c>resume</c>, <c>list</c>,
/// <c>close</c>), <c>session/prompt</c>, <c>session/cancel</c>, and the mode
/// and config option setters, and holds at most one live session. Opening,
/// replacing, and closing that session are serialized; prompts, cancels, and
/// setters run beside them.
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

    public async Task<JsonRpcReply> HandleRequestAsync(
        JsonElement id,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken) =>
        method switch
        {
            "initialize" => Initialize(parameters),
            "session/new" => await OpenAsync(id, Setup(parameters, resume: false), cancellationToken),
            "session/load" => await OpenAsync(id, Setup(parameters, resume: true) with { Replay = true }, cancellationToken),
            "session/resume" => await OpenAsync(id, Setup(parameters, resume: true), cancellationToken),
            "session/list" => List(parameters),
            "session/prompt" => await PromptAsync(parameters, cancellationToken),
            "session/set_mode" => await SetModeAsync(parameters),
            "session/set_config_option" => await SetOptionAsync(parameters),
            "session/close" => await CloseAsync(parameters),
            _ => throw new JsonRpcException(JsonRpcErrorCode.MethodNotFound, "Method not found: " + PromptContent.OneLine(method))
        };

    public Task HandleNotificationAsync(string method, JsonElement parameters)
    {
        if (method == "session/cancel"
            && JsonParams.OptionalString(parameters, "sessionId") is { } id
            && Volatile.Read(ref _active) is { } active
            && active.Answers(id))
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
            writer.WriteBoolean("loadSession", true);
            writer.WriteStartObject("promptCapabilities");
            writer.WriteBoolean("image", true);
            writer.WriteBoolean("audio", false);
            writer.WriteBoolean("embeddedContext", true);
            writer.WriteEndObject();
            writer.WriteStartObject("mcpCapabilities");
            writer.WriteBoolean("http", true);
            writer.WriteBoolean("sse", true);
            writer.WriteEndObject();
            writer.WriteStartObject("sessionCapabilities");
            foreach (var capability in new[] { "list", "resume", "close", "additionalDirectories" })
            {
                writer.WriteStartObject(capability);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartObject("_meta");
            writer.WriteStartObject("crystal");
            writer.WriteStartArray("extensions");
            writer.WriteEndArray();
            writer.WriteBoolean("loadAcrossCwd", true);
            writer.WriteBoolean("mutableWorkspace", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private SessionSetup Setup(JsonElement parameters, bool resume)
    {
        var cwd = JsonParams.OptionalString(parameters, "cwd");
        if (cwd is not null && !Path.IsPathFullyQualified(cwd))
        {
            throw JsonRpcException.InvalidParams("cwd must be an absolute path.");
        }

        var roots = new List<string>();
        if (JsonParams.OptionalArray(parameters, "additionalDirectories") is { } additional)
        {
            foreach (var entry in additional.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || !Path.IsPathFullyQualified(entry.GetString()!))
                {
                    throw JsonRpcException.InvalidParams("Each additional directory must be an absolute path.");
                }

                roots.Add(entry.GetString()!);
            }
        }

        var servers = McpServerWire.Read(JsonParams.OptionalArray(parameters, "mcpServers"));
        if (!resume)
        {
            return new SessionSetup(cwd, servers, roots);
        }

        var id = JsonParams.RequiredString(parameters, "sessionId");
        return new SessionSetup(cwd, servers, roots, id);
    }

    private async Task<JsonRpcReply> OpenAsync(JsonElement requestId, SessionSetup setup, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            // ACP may name the session by its ACP id; the engine stores the
            // id it has now, which /clear may have changed.
            var resumeId = setup.ResumeId is { } named && Volatile.Read(ref _active) is { } current && current.Answers(named)
                ? current.Session!.SessionId
                : setup.ResumeId;
            await CloseActiveAsync();
            var bridge = await StartAsync(requestId, setup with { ResumeId = resumeId }, cancellationToken);
            Volatile.Write(ref _active, bridge);
            var id = bridge.Id;
            var resumed = setup.ResumeId is not null;
            return new JsonRpcReply(
                writer =>
                {
                    writer.WriteStartObject();
                    if (!resumed)
                    {
                        writer.WriteString("sessionId", id);
                    }

                    bridge.Updates.WriteConfiguration(writer);
                    writer.WriteEndObject();
                },
                setup.Replay ? null : bridge.Release);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<BridgeSession> StartAsync(JsonElement requestId, SessionSetup setup, CancellationToken cancellationToken)
    {
        var request = new SessionLaunchRequest(
            _options.Launch,
            _options.WorkingDirectory,
            setup.Cwd ?? _options.Workspace,
            setup.Cwd is null && _options.Space,
            Resume: setup.ResumeId is not null,
            ResumeTarget: setup.ResumeId,
            Trust: SessionTrustCheck.Prompt,
            PersistSelection: true,
            AdditionalRoots: setup.AdditionalRoots);
        var trust = _features.Forms ? new LaunchTrustPrompt(_connection, requestId) : null;
        var prepared = await SessionLauncher.PrepareAsync(request, trust, chooser: null, cancellationToken);
        if (prepared.Launch is not { } launch)
        {
            throw prepared.Failure switch
            {
                SessionLaunchFailureKind.TrustNeedsOperator => JsonRpcException.Refused("trust_refused", UntrustedText(setup.Cwd)),
                SessionLaunchFailureKind.Declined => JsonRpcException.Refused(
                    "trust_refused",
                    "The directory was not trusted, so the session was not opened."),
                _ when setup.ResumeId is not null && prepared.Error.StartsWith("Session not found", StringComparison.Ordinal) =>
                    JsonRpcException.NotFound(prepared.Error),
                // A bad additional root is the client's parameter, not an engine refusal.
                _ when setup.AdditionalRoots.Count > 0 && IsRootError(prepared.Error) =>
                    JsonRpcException.InvalidParams(prepared.Error),
                _ => JsonRpcException.Refused(
                    "launch_refused",
                    prepared.Error.Length > 0 ? prepared.Error : "The session could not be opened.")
            };
        }

        foreach (var note in launch.Notes)
        {
            await _options.Diagnostics.WriteLineAsync(note);
        }

        // A resumed session keeps the ACP id the client knows it by.
        var bridge = new BridgeSession(_connection, _features, setup.ResumeId);
        CodingSession session;
        try
        {
            session = launch.CreateSession(
                new SessionFrontEnd(bridge, bridge, bridge, bridge, bridge)
                {
                    ChooseChildAsync = bridge.ChooseChildAsync,
                    McpServers = setup.McpServers
                },
                _options.Plugins);
        }
        catch (Exception exception) when (IsSetupFailure(exception))
        {
            throw JsonRpcException.Refused("launch_refused", exception.Message);
        }

        bridge.Bind(session, launch.Workspace, launch.Home);
        if (setup.Replay)
        {
            // session/load streams the history before it answers.
            bridge.Release();
        }
        else if (setup.ResumeId is not null)
        {
            bridge.DropStartReplay();
        }

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
        finally
        {
            bridge.EndStartReplay();
        }

        bridge.Run(() => _ = CloseIfActiveAsync(bridge));
        return bridge;
    }

    private static bool IsRootError(string error) =>
        error.StartsWith("Additional directory not found", StringComparison.Ordinal)
        || error.StartsWith("An additional directory must be outside the workspace", StringComparison.Ordinal)
        || error.StartsWith("Each additional directory", StringComparison.Ordinal)
        || error.Equals("Credential directories cannot be granted access.", StringComparison.Ordinal);

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

    private JsonRpcReply List(JsonElement parameters)
    {
        var cwd = JsonParams.OptionalString(parameters, "cwd");
        if (cwd is not null && !Path.IsPathFullyQualified(cwd))
        {
            throw JsonRpcException.InvalidParams("cwd must be an absolute path.");
        }

        var cursor = JsonParams.OptionalString(parameters, "cursor");
        var store = new SessionStore(CrystalHome.Resolve(_options.Launch.Home));
        var sessions = store.List(cwd);
        return new JsonRpcReply(writer => SessionList.Write(writer, sessions, cursor, summary => OutsideRoots(store, summary)));
    }

    /// <summary>The saved write rules outside the session's workspace.</summary>
    private static IReadOnlyList<string> OutsideRoots(SessionStore store, SessionSummary summary)
    {
        if (!store.TryLoad(summary.Id, out var document))
        {
            return [];
        }

        var root = Path.TrimEndingDirectorySeparator(summary.Workspace) + Path.DirectorySeparatorChar;
        return [.. document.Access
            .Where(rule => rule.Mode == "write"
                && !(rule.Path + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.Ordinal))
            .Select(static rule => rule.SourcePath ?? rule.Path)];
    }

    private async Task<JsonRpcReply> SetModeAsync(JsonElement parameters)
    {
        var bridge = Active(parameters);
        var mode = JsonParams.RequiredString(parameters, "modeId");
        var options = bridge.Updates.Options;
        var command = options.FirstOrDefault(static option => option.Id == "mode") is { } modeOption
            ? ConfigWire.Command(modeOption, JsonDocument.Parse(JsonSerializer.Serialize(mode)).RootElement)
            : null;
        if (command is null)
        {
            throw JsonRpcException.InvalidParams("modeId is not one of the available modes.");
        }

        await RunCommandAsync(bridge, command);
        return JsonRpcReply.Empty;
    }

    /// <summary>
    /// Runs the option's slash command, so validation, refusal while busy,
    /// and <c>config.json</c> writes match the terminal. Returns every option.
    /// </summary>
    private async Task<JsonRpcReply> SetOptionAsync(JsonElement parameters)
    {
        var bridge = Active(parameters);
        var id = JsonParams.RequiredString(parameters, "configId");
        if (!parameters.TryGetProperty("value", out var value))
        {
            throw JsonRpcException.InvalidParams("value is required.");
        }

        var option = bridge.Updates.Options.FirstOrDefault(option => option.Id == id)
            ?? throw JsonRpcException.InvalidParams("configId is not a config option of this session.");
        var command = ConfigWire.Command(option, value)
            ?? throw JsonRpcException.InvalidParams("value is not offered for " + option.Name + ".");
        await RunCommandAsync(bridge, command);
        return new JsonRpcReply(writer =>
        {
            writer.WriteStartObject();
            ConfigWire.Options(writer, "configOptions", bridge.Updates.Options, _features.BooleanOptions);
            writer.WriteEndObject();
        });
    }

    private async Task RunCommandAsync(BridgeSession bridge, string command)
    {
        var origin = NextOrigin();
        try
        {
            await bridge.Runner!.SubmitAsync(command, origin);
        }
        catch (InvalidOperationException)
        {
            throw JsonRpcException.NotFound("The session is not active.");
        }

        if (bridge.TakeError(origin) is { } refused)
        {
            throw JsonRpcException.Refused("command_refused", refused);
        }
    }

    private string NextOrigin() =>
        "prompt-" + Interlocked.Increment(ref _prompts).ToString(CultureInfo.InvariantCulture);

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
        var origin = NextOrigin();
        var content = await PromptContent.ReadAsync(
            prompt,
            bytes => runner.InvokeAsync(session => Attached(session.TryAttachImage(bytes, out var image, out var error), image, error), origin),
            path => runner.InvokeAsync(session => Attached(session.TryAttachImageFile(path, out var image, out var error), image, error), origin));
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

        bridge.Saved();
        var stop = cancelled ? TurnWire.Cancelled : TurnWire.StopReason(finished.Result.StopReason);
        if (stop is null)
        {
            var kind = finished.Result.StopReason == TurnStopReason.ContextOverflow ? "context_overflow" : "turn_failed";
            throw JsonRpcException.Refused(kind, Failure(outcome.Error), writer => TurnWire.Crystal(writer, finished));
        }

        return Reply(stop, finished);
    }

    private static ImageAttachResult Attached(bool attached, ImageAttachment? image, string error) =>
        attached ? new ImageAttachResult(image!.TrustedMarker, string.Empty) : new ImageAttachResult(null, error);

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
        return Volatile.Read(ref _active) is { } active && active.Answers(id)
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
