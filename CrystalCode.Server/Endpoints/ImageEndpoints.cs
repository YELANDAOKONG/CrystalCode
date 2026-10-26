using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Hosting;
using CrystalCode.Server.Security;

namespace CrystalCode.Server.Endpoints;

/// <summary>
/// <c>POST /images</c> attaches uploaded bytes to the caller's draft and
/// returns the trusted marker to put in the submitted text.
/// <c>GET /images/{number}</c> returns a held session image.
/// </summary>
internal static class ImageEndpoints
{
    public static void Map(WebApplication app, ServerContext server)
    {
        app.MapPost("/images", async (HttpContext context, CancellationToken token) =>
        {
            if (AccessGuard.Scope(context) < ServerScope.Operate)
            {
                return Json.Problem(403, "This request needs the operate scope.");
            }

            if (server.Session.Runner is not { } runner)
            {
                return Json.Problem(503, "The session is not ready.");
            }

            var data = await ReadBytesAsync(context.Request, token);
            if (data is null)
            {
                return Json.Problem(413, "The image exceeds the 20 MiB host limit.");
            }

            var client = AccessGuard.Client(context);
            var requestId = context.Request.Query["clientRequestId"].ToString();
            var outcome = await server.Requests.RunOnceAsync(client, requestId, async () =>
            {
                var (attached, image, error) = await runner.InvokeAsync(session =>
                {
                    var ok = session.TryAttachImage(data, out var value, out var message);
                    return (ok, value, message);
                }, client);
                return attached && image is not null
                    ? RequestOutcome.Ok(Json.Write(writer =>
                    {
                        writer.WriteNumber("number", image.Number);
                        writer.WriteString("mimeType", image.MimeType);
                        writer.WriteString("marker", image.Marker);
                        writer.WriteString("trustedMarker", image.TrustedMarker);
                    }))
                    : RequestOutcome.Error(400, error);
            });
            return outcome.Problem is not null
                ? Json.Problem(outcome.Status, outcome.Problem)
                : Results.Text(outcome.Json!, "application/json");
        });

        app.MapGet("/images/{number}", (HttpContext context, string number) =>
            ServerEndpoints.Direct(context, server, ServerScope.Observe, session =>
            {
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    || !session.TryGetImage(value, out var image)
                    || image is null)
                {
                    return Json.Problem(404, "The session does not hold that image.");
                }

                if (image.Data is { } bytes)
                {
                    return Results.Bytes(bytes.ToArray(), image.MimeType);
                }

                return Json.Ok(writer =>
                {
                    writer.WriteString("mimeType", image.MimeType);
                    writer.WriteString("uri", image.Uri!.AbsoluteUri);
                });
            }));
    }

    /// <summary>Returns null when the body exceeds the host image limit.</summary>
    private static async Task<byte[]?> ReadBytesAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > ImageFile.MaximumBytes)
        {
            return null;
        }

        using var stream = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (stream.Length + read > ImageFile.MaximumBytes)
            {
                return null;
            }

            stream.Write(buffer, 0, read);
        }

        return stream.ToArray();
    }
}
