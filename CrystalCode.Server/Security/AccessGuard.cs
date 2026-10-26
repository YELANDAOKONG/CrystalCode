using System.Collections.Concurrent;
using System.Net;

using Microsoft.AspNetCore.Http;

namespace CrystalCode.Server.Security;

/// <summary>
/// Runs before every endpoint. Checks the Host header against loopback, the
/// listen address, and <c>--allow-host</c> (DNS rebinding), rejects an
/// Origin that is not allowed, then authenticates the bearer token or a
/// single-use event-stream ticket. Repeated failures from one address are
/// slowed down. <c>/health</c> needs no token. With authentication off,
/// every request has the admin scope.
/// </summary>
public sealed class AccessGuard
{
    public const string ScopeItem = "crystal.scope";

    public const string ClientItem = "crystal.client";

    public const string ClientHeader = "X-Crystal-Client";

    private const int FailureLimit = 20;

    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);

    private readonly TokenStore? _tokens;
    private readonly HashSet<string> _hosts;
    private readonly HashSet<string> _origins;
    private readonly ConcurrentDictionary<string, Failures> _failures = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public AccessGuard(TokenStore? tokens, IEnumerable<string> hosts, IEnumerable<string> origins, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(origins);
        _tokens = tokens;
        _hosts = new HashSet<string>(hosts.Select(Normalize), StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "::1"
        };
        _origins = new HashSet<string>(origins.Select(static origin => origin.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);
        _time = time ?? TimeProvider.System;
    }

    public void AllowHost(string host) => _hosts.Add(Normalize(host));

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var request = context.Request;
        // A Unix socket has no Host to rebind; Kestrel reports "localhost".
        if (!HostAllowed(request.Host))
        {
            await Problems.WriteAsync(context, StatusCodes.Status421MisdirectedRequest, "The Host header is not allowed.");
            return;
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !_origins.Contains(origin.TrimEnd('/')))
        {
            await Problems.WriteAsync(context, StatusCodes.Status403Forbidden, "The request Origin is not allowed.");
            return;
        }

        if (origin.Length > 0)
        {
            context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Vary = "Origin";
            if (HttpMethods.IsOptions(request.Method))
            {
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, DELETE, OPTIONS";
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, X-Crystal-Client, Last-Event-ID";
                context.Response.Headers.AccessControlMaxAge = "600";
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
        }

        context.Items[ClientItem] = ClientId(context);
        if (request.Path.Equals("/health", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        if (_tokens is null)
        {
            context.Items[ScopeItem] = ServerScope.Admin;
            await next(context);
            return;
        }

        var address = context.Connection.RemoteIpAddress?.ToString() ?? "local";
        if (Limited(address))
        {
            context.Response.Headers.RetryAfter = "60";
            await Problems.WriteAsync(context, StatusCodes.Status429TooManyRequests, "Too many failed authentication attempts.");
            return;
        }

        if (!TryAuthenticate(request, out var token))
        {
            RecordFailure(address);
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, "A valid bearer token is required.");
            return;
        }

        context.Items[ScopeItem] = token.Scope;
        context.Items["crystal.token"] = token;
        await next(context);
    }

    public static ServerScope Scope(HttpContext context) =>
        context.Items.TryGetValue(ScopeItem, out var scope) && scope is ServerScope value ? value : ServerScope.Observe;

    public static string Client(HttpContext context) =>
        context.Items.TryGetValue(ClientItem, out var client) && client is string value ? value : "anonymous";

    public static ServerToken? Token(HttpContext context) =>
        context.Items.TryGetValue("crystal.token", out var token) ? token as ServerToken : null;

    private bool TryAuthenticate(HttpRequest request, out ServerToken token)
    {
        token = null!;
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return _tokens!.TryAuthenticate(header["Bearer ".Length..].Trim(), out token);
        }

        // Browsers cannot set headers on EventSource; only the stream accepts a ticket.
        return request.Path.Equals("/events", StringComparison.Ordinal)
            && _tokens!.TryRedeemTicket(request.Query["ticket"].ToString(), out token);
    }

    private bool HostAllowed(HostString host)
    {
        if (!host.HasValue)
        {
            return false;
        }

        return _hosts.Contains(Normalize(host.Host));
    }

    private static string ClientId(HttpContext context)
    {
        var header = context.Request.Headers[ClientHeader].ToString().Trim();
        if (header.Length > 0 && header.Length <= 128 && header.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            return header;
        }

        var query = context.Request.Query["client"].ToString().Trim();
        return query.Length > 0 && query.Length <= 128 && query.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? query
            : "anonymous";
    }

    private static string Normalize(string host)
    {
        var text = host.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            text = text[1..^1];
        }

        return IPAddress.TryParse(text, out var address) ? address.ToString() : text.ToLowerInvariant();
    }

    private bool Limited(string address) =>
        _failures.TryGetValue(address, out var failures)
        && failures.Count >= FailureLimit
        && failures.Since + FailureWindow > _time.GetUtcNow();

    private void RecordFailure(string address)
    {
        var now = _time.GetUtcNow();
        _failures.AddOrUpdate(
            address,
            _ => new Failures(1, now),
            (_, previous) => previous.Since + FailureWindow <= now
                ? new Failures(1, now)
                : previous with { Count = previous.Count + 1 });
    }

    private sealed record Failures(int Count, DateTimeOffset Since);
}
