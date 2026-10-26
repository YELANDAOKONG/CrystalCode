using CrystalCode.Engine.Launch;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// Everything <c>crystal server</c> was started with. Secrets never arrive
/// here as command flags: the token comes from the environment, an
/// owner-only file, or stdin, and is resolved before this value exists.
/// </summary>
public sealed record ServerOptions
{
    public const string DefaultListen = "127.0.0.1:0";

    public required SessionLaunchRequest Launch { get; init; }

    /// <summary><c>host:port</c>. Port 0 picks a free port.</summary>
    public string Listen { get; init; } = DefaultListen;

    /// <summary>A Unix domain socket path instead of TCP.</summary>
    public string? Socket { get; init; }

    public string? TlsCertificate { get; init; }

    public string? TlsKey { get; init; }

    /// <summary>The startup token, or null when authentication is off.</summary>
    public string? Token { get; init; }

    /// <summary>True when the token was generated and belongs in the discovery file.</summary>
    public bool TokenGenerated { get; init; }

    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>Event ring buffer bounds.</summary>
    public int EventLogCount { get; init; } = 20_000;

    public long EventLogBytes { get; init; } = 32L * 1024 * 1024;

    public TimeSpan Heartbeat { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Write the discovery file under <c>~/.crystal/server</c>.</summary>
    public bool WriteDiscovery { get; init; } = true;

    public bool AuthenticationEnabled => Token is not null;
}
