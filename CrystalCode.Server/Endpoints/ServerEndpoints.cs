using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Version;
using CrystalCode.Server.Hosting;
using CrystalCode.Server.Interactions;
using CrystalCode.Server.Security;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Endpoints;

/// <summary>
/// Every HTTP route the server exposes. Each route checks its scope, then
/// calls the session through <see cref="SessionRunner"/> under the client's
/// origin, or a direct session method for the operations Ctrl+C makes from
/// any thread. The routes hold no product logic.
/// </summary>
public static class ServerEndpoints
{
    /// <summary>Route names exposed by <c>/info</c>.</summary>
    public static IReadOnlyList<string> Operations { get; } =
    [
        "GET /health", "GET /info", "GET /snapshot", "GET /events",
        "POST /input", "POST /interrupt", "POST /recall", "POST /mode", "POST /verbose", "PUT /draft",
        "POST /images", "GET /images/{number}", "GET /quick-prompts", "POST /quick-prompts/{name}/send",
        "POST /side/cancel", "POST /side/clear", "GET /children", "GET /children/{id}", "GET /export",
        "GET /interactions", "POST /interactions/{id}", "POST /auth/ticket", "GET /auth/tokens",
        "POST /auth/tokens", "DELETE /auth/tokens/{id}", "POST /shutdown"
    ];

    public static void Map(WebApplication app, ServerContext server)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(server);

        app.MapGet("/health", () => Json.Ok(writer => writer.WriteString("state", server.Session.State)));

        app.MapGet("/info", (HttpContext context) => Json.Ok(writer =>
        {
            writer.WriteString("product", "Crystal Code");
            writer.WriteStartObject("build");
            Build(writer, server.Runtime.Build);
            writer.WriteEndObject();
            writer.WriteString("state", server.Session.State);
            WireWriter.Optional(writer, "error", server.Session.Failure);
            WireWriter.Optional(writer, "sessionId", server.Session.Session?.SessionId);
            WireWriter.Optional(writer, "workspace", server.Session.Launch?.Workspace);
            writer.WriteString("scope", ServerScopes.Name(AccessGuard.Scope(context)));
            writer.WriteBoolean("authentication", server.Options.AuthenticationEnabled);
            WireWriter.Strings(writer, "operations", Operations);
            WireWriter.Strings(writer, "eventTypes", WireEvents.SessionTypes.Values
                .Where(static type => type != "stream")
                .Concat(WireEvents.StreamTypes.Values)
                .Concat(ServerEventTypes)
                .Order(StringComparer.Ordinal));
            WireWriter.Strings(writer, "interactionKinds", InteractionKind.All);
            WireWriter.Strings(writer, "slashCommands", SlashCatalog.BuiltIn.Select(static spec => spec.Name));
        }));

        app.MapGet("/snapshot", (HttpContext context) =>
        {
            var client = AccessGuard.Client(context);
            return Results.Text(Json.Write(writer =>
                server.Session.Hub.WriteSnapshot(writer, (inner, snapshot) =>
                    SnapshotWriter.Write(inner, snapshot, server.Session.State, server.Session.Broker.Pending, client))),
                "application/json",
                Encoding.UTF8);
        });

        app.MapGet("/events", EventStream.HandleAsync(server));

        app.MapPost("/input", (HttpContext context, CancellationToken token) =>
            Mutate(context, server, ServerScope.Operate, token, async (body, runner, client) =>
            {
                var text = Json.String(body, "text");
                if (text is null)
                {
                    return RequestOutcome.Error(400, "text is required. Send an empty string to release the queue.");
                }

                var needed = ServerScopes.ForInput(text);
                if (needed > AccessGuard.Scope(context))
                {
                    return RequestOutcome.Error(403, $"This command needs the {ServerScopes.Name(needed)} scope.");
                }

                var result = await runner.SubmitAsync(text, client);
                return RequestOutcome.Ok(Json.Write(writer =>
                {
                    writer.WriteBoolean("accepted", result.Accepted);
                    writer.WriteBoolean("pending", result.Pending);
                    WireWriter.Optional(writer, "error", result.Error);
                    writer.WriteBoolean("turnActive", result.TurnActive);
                    writer.WriteBoolean("quit", result.Quit);
                }));
            }));

        app.MapPost("/interrupt", (HttpContext context) =>
            Direct(context, server, ServerScope.Operate, session => Json.Ok(writer =>
                writer.WriteBoolean("interrupted", session.TryInterrupt()))));

        app.MapPost("/recall", (HttpContext context) =>
            Direct(context, server, ServerScope.Operate, session => Json.Ok(writer =>
                writer.WriteBoolean("recalled", session.TryRecall()))));

        app.MapPost("/mode", (HttpContext context, CancellationToken token) =>
            Mutate(context, server, ServerScope.Operate, token, async (body, runner, client) =>
            {
                var action = Json.String(body, "action")?.Trim().ToLowerInvariant();
                var name = Json.String(body, "name");
                var (ok, error) = await runner.InvokeAsync<(bool, string)>(session => action switch
                {
                    "cycle" => (session.CycleMode() || true, string.Empty),
                    "toggle-plan" => (session.TogglePlan() || true, string.Empty),
                    "set" when !string.IsNullOrWhiteSpace(name) => session.TrySetMode(name, out var modeError)
                        ? (true, string.Empty)
                        : (false, modeError),
                    _ => (false, "action must be cycle, toggle-plan, or set with a name.")
                }, client);
                return ok
                    ? RequestOutcome.Ok(Json.Write(writer => ModeFields(writer, runner.Session)))
                    : RequestOutcome.Error(400, error);
            }));

        app.MapPost("/verbose", (HttpContext context, CancellationToken token) =>
            Mutate(context, server, ServerScope.Operate, token, async (body, runner, client) =>
            {
                var target = Json.String(body, "target");
                var enabled = Json.Boolean(body, "enabled");
                if (!Enum.TryParse<VerboseTarget>(target, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed)
                    || enabled is null)
                {
                    return RequestOutcome.Error(400,
                        "target must be tools, commands, results, calls, approvals, review, or thinking, and enabled must be true or false.");
                }

                await runner.InvokeAsync(session =>
                {
                    session.SetVerbose(parsed, enabled.Value);
                    return true;
                }, client);
                return RequestOutcome.Ok(Json.Write(writer => writer.WriteBoolean("enabled", enabled.Value)));
            }));

        app.MapPut("/draft", (HttpContext context, CancellationToken token) =>
            Mutate(context, server, ServerScope.Operate, token, async (body, runner, client) =>
            {
                var text = Json.String(body, "text");
                if (text is null)
                {
                    return RequestOutcome.Error(400, "text is required.");
                }

                await runner.InvokeAsync(session =>
                {
                    session.NotifyDraftChanged(text);
                    return true;
                }, client);
                return RequestOutcome.Ok("{}");
            }));

        ImageEndpoints.Map(app, server);
        SessionEndpoints.Map(app, server);
        AuthEndpoints.Map(app, server);
    }

    public static IReadOnlyList<string> ServerEventTypes { get; } =
    [
        "server.state", "server.closing", "interaction.opened", "interaction.resolved", "interaction.cancelled", "resync"
    ];

    internal static void ModeFields(Utf8JsonWriter writer, CodingSession session)
    {
        writer.WriteBoolean("planMode", session.PlanMode);
        writer.WriteString("mode", session.CustomMode ?? (session.PlanMode ? "plan" : "work"));
        WireWriter.Pending(writer, "pending", session.PendingSettings);
    }

    internal static IResult Direct(
        HttpContext context,
        ServerContext server,
        ServerScope scope,
        Func<CodingSession, IResult> action)
    {
        if (AccessGuard.Scope(context) < scope)
        {
            return Json.Problem(403, $"This request needs the {ServerScopes.Name(scope)} scope.");
        }

        return server.Session.Session is { } session
            ? action(session)
            : Json.Problem(503, "The session is not ready.");
    }

    /// <summary>
    /// Runs a state-changing request once per <c>clientRequestId</c>, after
    /// the scope check, with the body parsed.
    /// </summary>
    internal static async Task<IResult> Mutate(
        HttpContext context,
        ServerContext server,
        ServerScope scope,
        CancellationToken cancellationToken,
        Func<JsonElement, SessionRunner, string, Task<RequestOutcome>> action)
    {
        if (AccessGuard.Scope(context) < scope)
        {
            return Json.Problem(403, $"This request needs the {ServerScopes.Name(scope)} scope.");
        }

        if (server.Session.Runner is not { } runner)
        {
            return Json.Problem(503, "The session is not ready.");
        }

        var (body, error) = await Json.ReadAsync(context.Request, cancellationToken);
        if (error is not null)
        {
            return Json.Problem(400, error);
        }

        var client = AccessGuard.Client(context);
        try
        {
            var outcome = await server.Requests.RunOnceAsync(
                client,
                Json.String(body, "clientRequestId"),
                () => action(body, runner, client));
            return outcome.Problem is not null
                ? Json.Problem(outcome.Status, outcome.Problem)
                : Results.Text(outcome.Json ?? "{}", "application/json", Encoding.UTF8, outcome.Status);
        }
        catch (InvalidOperationException exception)
        {
            return Json.Problem(409, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Json.Problem(400, exception.Message);
        }
        catch (OperationCanceledException)
        {
            return Json.Problem(503, "The session is closing.");
        }
    }

    private static void Build(Utf8JsonWriter writer, BuildIdentity build)
    {
        WireWriter.Optional(writer, "crystalCode", build.ProductRevision);
        WireWriter.Optional(writer, "crystal", build.LibraryRevision);
        WireWriter.Optional(writer, "sdk", build.SdkVersion);
        WireWriter.Optional(writer, "configuration", build.Configuration);
        WireWriter.Optional(writer, "runtime", build.Runtime);
        WireWriter.Optional(writer, "os", build.OperatingSystem);
    }

    internal static void WriteBuild(Utf8JsonWriter writer, BuildIdentity build) => Build(writer, build);
}
