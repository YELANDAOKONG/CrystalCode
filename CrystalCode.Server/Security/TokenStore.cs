using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CrystalCode.Server.Security;

/// <summary>
/// Process-local tokens and single-use event-stream tickets. Nothing here is
/// persisted or logged. Comparisons are constant time.
/// </summary>
public sealed class TokenStore
{
    public const string PrimaryId = "primary";

    private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(30);
    private const int SecretBytes = 32;

    private readonly ConcurrentDictionary<string, ServerToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public TokenStore(string primarySecret, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primarySecret);
        _time = time ?? TimeProvider.System;
        _tokens[PrimaryId] = new ServerToken(PrimaryId, Hash(primarySecret), ServerScope.Admin, "Startup token");
    }

    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public IReadOnlyList<ServerToken> List() => [.. _tokens.Values.OrderBy(static token => token.Id, StringComparer.Ordinal)];

    public IssuedToken Issue(ServerScope scope, string? label)
    {
        var secret = NewSecret();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var text = string.IsNullOrWhiteSpace(label) ? "Issued token" : label.Trim();
        _tokens[id] = new ServerToken(id, Hash(secret), scope, text);
        return new IssuedToken(id, secret, scope, text);
    }

    /// <summary>The startup token cannot be revoked; it ends with the process.</summary>
    public bool Revoke(string id) =>
        !string.Equals(id, PrimaryId, StringComparison.Ordinal) && _tokens.TryRemove(id, out _);

    public bool TryAuthenticate(string? secret, out ServerToken token)
    {
        token = null!;
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var hash = Hash(secret);
        var matched = false;
        // Compare against every token so the time does not depend on which matched.
        foreach (var candidate in _tokens.Values)
        {
            if (CryptographicOperations.FixedTimeEquals(candidate.Hash, hash))
            {
                token = candidate;
                matched = true;
            }
        }

        return matched;
    }

    public string IssueTicket(ServerToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        PruneTickets();
        var ticket = NewSecret();
        _tickets[ticket] = new Ticket(token.Id, _time.GetUtcNow() + TicketLifetime);
        return ticket;
    }

    /// <summary>A ticket works once, for the token that asked for it, while that token is still valid.</summary>
    public bool TryRedeemTicket(string? ticket, out ServerToken token)
    {
        token = null!;
        if (string.IsNullOrEmpty(ticket) || !_tickets.TryRemove(ticket, out var entry))
        {
            return false;
        }

        return entry.Expires > _time.GetUtcNow() && _tokens.TryGetValue(entry.TokenId, out token!);
    }

    private void PruneTickets()
    {
        var now = _time.GetUtcNow();
        foreach (var pair in _tickets)
        {
            if (pair.Value.Expires <= now)
            {
                _tickets.TryRemove(pair.Key, out _);
            }
        }
    }

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private sealed record Ticket(string TokenId, DateTimeOffset Expires);
}
