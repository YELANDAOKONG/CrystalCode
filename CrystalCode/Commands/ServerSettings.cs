using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal server</c>. Session options behave as on
/// <c>crystal</c>; none of them, and none of these, are written to
/// <c>config.json</c>. No option carries a secret.
/// </summary>
public sealed class ServerSettings : SessionLaunchSettings
{
    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--space")]
    public bool Space { get; init; }

    [CommandOption("-r|--resume [TARGET]")]
    public required FlagValue<string?> Resume { get; init; }

    [CommandOption("--listen <HOST:PORT>")]
    public string? Listen { get; init; }

    [CommandOption("--socket <PATH>")]
    public string? Socket { get; init; }

    [CommandOption("--tls-cert <PATH>")]
    public string? TlsCert { get; init; }

    [CommandOption("--tls-key <PATH>")]
    public string? TlsKey { get; init; }

    [CommandOption("--auth <token|none>")]
    public string? Auth { get; init; }

    [CommandOption("--token-file <PATH>")]
    public string? TokenFile { get; init; }

    [CommandOption("--token-stdin")]
    public bool TokenStdin { get; init; }

    [CommandOption("--allow-host <HOST>")]
    public string[] AllowHost { get; init; } = [];

    [CommandOption("--allow-origin <ORIGIN>")]
    public string[] AllowOrigin { get; init; } = [];

    [CommandOption("--expose-header <NAME>")]
    public string[] ExposeHeader { get; init; } = [];

    [CommandOption("--allow-private-network")]
    public bool AllowPrivateNetwork { get; init; }

    [CommandOption("--exit-on-stdin-close")]
    public bool ExitOnStdinClose { get; init; }
}
