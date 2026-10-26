using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using CrystalCode.Server.Security;

namespace CrystalCode.Server.Endpoints;

/// <summary>Event-stream tickets, narrower tokens, and shutdown.</summary>
internal static class AuthEndpoints
{
    public static void Map(WebApplication app, ServerContext server)
    {
        app.MapPost("/auth/ticket", (HttpContext context) =>
        {
            if (server.Tokens is null)
            {
                return Json.Problem(409, "Authentication is off; the event stream needs no ticket.");
            }

            var token = AccessGuard.Token(context);
            if (token is null)
            {
                return Json.Problem(401, "A bearer token is required.");
            }

            var ticket = server.Tokens.IssueTicket(token);
            return Json.Ok(writer =>
            {
                writer.WriteString("ticket", ticket);
                writer.WriteNumber("expiresInSeconds", 30);
            });
        });

        app.MapGet("/auth/tokens", (HttpContext context) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Admin)
            {
                return Json.Problem(403, "This request needs the admin scope.");
            }

            if (server.Tokens is null)
            {
                return Json.Problem(409, "Authentication is off.");
            }

            return Json.Ok(writer =>
            {
                writer.WriteStartArray("tokens");
                foreach (var token in server.Tokens.List())
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", token.Id);
                    writer.WriteString("scope", ServerScopes.Name(token.Scope));
                    writer.WriteString("label", token.Label);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        });

        app.MapPost("/auth/tokens", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Admin)
            {
                return Json.Problem(403, "This request needs the admin scope.");
            }

            if (server.Tokens is null)
            {
                return Json.Problem(409, "Authentication is off.");
            }

            var (body, error) = await Json.ReadAsync(context.Request, cancellationToken);
            if (error is not null)
            {
                return Json.Problem(400, error);
            }

            if (!ServerScopes.TryParse(Json.String(body, "scope"), out var scope))
            {
                return Json.Problem(400, "scope must be observe, operate, or admin.");
            }

            var issued = server.Tokens.Issue(scope, Json.String(body, "label"));
            return Json.Ok(writer =>
            {
                writer.WriteString("id", issued.Id);
                writer.WriteString("token", issued.Secret);
                writer.WriteString("scope", ServerScopes.Name(issued.Scope));
                writer.WriteString("label", issued.Label);
            });
        });

        app.MapDelete("/auth/tokens/{id}", (HttpContext context, string id) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Admin)
            {
                return Json.Problem(403, "This request needs the admin scope.");
            }

            if (server.Tokens is null)
            {
                return Json.Problem(409, "Authentication is off.");
            }

            return server.Tokens.Revoke(id)
                ? Json.Ok(writer => writer.WriteBoolean("revoked", true))
                : Json.Problem(404, "No revocable token has that id.");
        });

        app.MapPost("/shutdown", (HttpContext context) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Admin)
            {
                return Json.Problem(403, "This request needs the admin scope.");
            }

            server.Shutdown();
            return Json.Ok(writer => writer.WriteBoolean("closing", true));
        });
    }
}
