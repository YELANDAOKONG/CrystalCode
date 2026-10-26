using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using CrystalCode.Engine.Home;
using CrystalCode.Server.Hosting;
using CrystalCode.Server.Security;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Endpoints;

/// <summary>
/// Quick prompts, side questions, child inspection, export, and interaction
/// answers.
/// </summary>
internal static class SessionEndpoints
{
    public static void Map(WebApplication app, ServerContext server)
    {
        app.MapGet("/quick-prompts", async (HttpContext context, CancellationToken token) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Observe)
            {
                return Json.Problem(403, "This request needs the observe scope.");
            }

            if (server.Session.QuickPrompts is not { } store)
            {
                return Json.Problem(503, "The session is not ready.");
            }

            var catalog = await store.LoadAsync(token);
            return Json.Ok(writer => QuickPrompts(writer, catalog));
        });

        app.MapPost("/quick-prompts/{name}/send", (HttpContext context, string name, CancellationToken token) =>
            ServerEndpoints.Mutate(context, server, ServerScope.Operate, token, async (_, runner, client) =>
            {
                var catalog = await server.Session.QuickPrompts!.LoadAsync(token);
                var prompt = catalog.Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
                if (prompt is null)
                {
                    return RequestOutcome.Error(404, "Quick prompt not found  " + name);
                }

                if (prompt.Error is not null)
                {
                    return RequestOutcome.Error(400, prompt.Error);
                }

                var result = await runner.SubmitQuickPromptAsync(prompt.Text, client);
                return RequestOutcome.Ok(Json.Write(writer => writer.WriteBoolean("turnActive", result.TurnActive)));
            }));

        app.MapPost("/side/cancel", (HttpContext context) =>
            ServerEndpoints.Direct(context, server, ServerScope.Operate, session => Json.Ok(writer =>
                writer.WriteBoolean("cancelled", session.TryCancelSideQuestion()))));

        app.MapPost("/side/clear", (HttpContext context, CancellationToken token) =>
            ServerEndpoints.Mutate(context, server, ServerScope.Operate, token, async (_, runner, client) =>
            {
                await runner.InvokeAsync(session =>
                {
                    session.ClearSideQuestions();
                    return true;
                }, client);
                return RequestOutcome.Ok("{}");
            }));

        app.MapGet("/children", (HttpContext context) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Observe)
            {
                return Json.Problem(403, "This request needs the observe scope.");
            }

            var children = server.Session.Hub.Snapshot().Children;
            return Json.Ok(writer =>
            {
                writer.WriteStartArray("children");
                foreach (var child in children)
                {
                    WireWriter.Child(writer, child, full: false);
                }

                writer.WriteEndArray();
            });
        });

        app.MapGet("/children/{id}", (HttpContext context, string id) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Observe)
            {
                return Json.Problem(403, "This request needs the observe scope.");
            }

            var child = server.Session.Hub.Snapshot().Children
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            return child is null
                ? Json.Problem(404, "No matching child session.")
                : Results.Text(Json.Write(writer =>
                {
                    writer.WritePropertyName("child");
                    WireWriter.Child(writer, child, full: true);
                }), "application/json");
        });

        app.MapGet("/export", async (HttpContext context) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Observe)
            {
                return Json.Problem(403, "This request needs the observe scope.");
            }

            if (server.Session.Runner is not { } runner)
            {
                return Json.Problem(503, "The session is not ready.");
            }

            var format = context.Request.Query["format"].ToString();
            var system = string.Equals(context.Request.Query["system"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            var (ok, content, error) = await runner.InvokeAsync(session =>
            {
                var exported = session.TryExport(format.Length == 0 ? "markdown" : format, system, out var value, out var message);
                return (exported, value, message);
            }, AccessGuard.Client(context));
            if (!ok || content is null)
            {
                return Json.Problem(409, error);
            }

            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{content.FileName}\"";
            return Results.Text(content.Text, content.MediaType + "; charset=utf-8");
        });

        app.MapGet("/interactions", (HttpContext context) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Observe)
            {
                return Json.Problem(403, "This request needs the observe scope.");
            }

            var client = AccessGuard.Client(context);
            return Json.Ok(writer =>
            {
                writer.WriteStartArray("interactions");
                foreach (var interaction in server.Session.Broker.Pending)
                {
                    if (interaction.Target is null || string.Equals(interaction.Target, client, StringComparison.Ordinal))
                    {
                        interaction.Write(writer);
                    }
                }

                writer.WriteEndArray();
            });
        });

        app.MapPost("/interactions/{id}", async (HttpContext context, string id, CancellationToken token) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Operate)
            {
                return Json.Problem(403, "This request needs the operate scope.");
            }

            if (server.Session.Broker.TryGet(id, out var pending)
                && pending.Kind == Interactions.InteractionKind.Trust
                && AccessGuard.Scope(context) < ServerScope.Admin)
            {
                return Json.Problem(403, "Trusting a directory needs the admin scope.");
            }

            var (body, error) = await Json.ReadAsync(context.Request, token);
            if (error is not null)
            {
                return Json.Problem(400, error);
            }

            if (!server.Session.Broker.TryAnswer(id, body, AccessGuard.Client(context), out var conflict, out var answerError))
            {
                return Json.Problem(conflict ? 409 : 400, answerError);
            }

            return Json.Ok(writer => writer.WriteBoolean("resolved", true));
        });
    }

    private static void QuickPrompts(System.Text.Json.Utf8JsonWriter writer, QuickPromptCatalog catalog)
    {
        writer.WriteString("directory", catalog.Directory);
        WireWriter.Optional(writer, "error", catalog.Error);
        writer.WriteStartArray("prompts");
        foreach (var entry in catalog.Entries)
        {
            writer.WriteStartObject();
            writer.WriteString("name", entry.Name);
            writer.WriteString("text", entry.Text);
            WireWriter.Optional(writer, "error", entry.Error);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
