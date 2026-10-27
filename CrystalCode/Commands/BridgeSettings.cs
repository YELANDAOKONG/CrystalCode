using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal bridge</c>. Session options behave as on
/// <c>crystal</c> and are not written to <c>config.json</c>. The workspace
/// options only apply to a <c>session/new</c> without <c>cwd</c>. No option
/// carries a secret.
/// </summary>
public sealed class BridgeSettings : SessionLaunchSettings
{
    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--space")]
    public bool Space { get; init; }
}
