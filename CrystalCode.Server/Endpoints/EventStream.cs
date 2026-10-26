using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Http;

using CrystalCode.Server.Security;

namespace CrystalCode.Server.Endpoints;

/// <summary>
/// <c>GET /events</c>: a server-sent event stream. Each message carries its
/// sequence number as the SSE id, so a browser reconnects with
/// <c>Last-Event-ID</c>. A cursor that has left the buffer gets a
/// <c>resync</c> event and the stream ends; the client fetches a new
/// snapshot. A comment heartbeat keeps proxies from closing an idle stream.
/// </summary>
internal static class EventStream
{
    public static Func<HttpContext, Task> HandleAsync(ServerContext server) => async context =>
    {
        var client = AccessGuard.Client(context);
        var after = Cursor(context.Request);
        var response = context.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        var subscription = server.Session.Hub.Subscribe(after, client);
        if (subscription is null)
        {
            await WriteAsync(response, "retry: 2000\nevent: resync\ndata: {\"type\":\"resync\"}\n\n", context.RequestAborted);
            return;
        }

        using (subscription)
        using (server.Clients.Connect(client))
        {
            await WriteAsync(response, "retry: 2000\n\n", context.RequestAborted);
            using var heartbeat = new PeriodicTimer(server.Options.Heartbeat);
            var tick = heartbeat.WaitForNextTickAsync(context.RequestAborted).AsTask();
            var read = subscription.Reader.WaitToReadAsync(context.RequestAborted).AsTask();
            try
            {
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    var finished = await Task.WhenAny(read, tick);
                    if (finished == tick)
                    {
                        if (!await tick)
                        {
                            return;
                        }

                        await WriteAsync(response, ": ping\n\n", context.RequestAborted);
                        tick = heartbeat.WaitForNextTickAsync(context.RequestAborted).AsTask();
                        continue;
                    }

                    if (!await read)
                    {
                        return;
                    }

                    var batch = new StringBuilder();
                    while (subscription.Reader.TryRead(out var item))
                    {
                        batch.Append("id: ").Append(item.Sequence.ToString(CultureInfo.InvariantCulture)).Append('\n');
                        batch.Append("event: ").Append(item.Type).Append('\n');
                        batch.Append("data: ").Append(item.Json).Append("\n\n");
                    }

                    await WriteAsync(response, batch.ToString(), context.RequestAborted);
                    read = subscription.Reader.WaitToReadAsync(context.RequestAborted).AsTask();
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away.
            }
        }
    };

    private static long Cursor(HttpRequest request)
    {
        var text = request.Headers["Last-Event-ID"].ToString();
        if (text.Length == 0)
        {
            text = request.Query["after"].ToString();
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static async Task WriteAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.WriteAsync(text, Encoding.UTF8, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
