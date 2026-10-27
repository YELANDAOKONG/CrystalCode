using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal bridge</c>. Session options behave as on
/// <c>crystal</c> and are not written to <c>config.json</c>. The workspace
/// options only apply to a <c>session/new</c> without <c>cwd</c>. The config
/// option flags choose which session options the editor sees for this process
/// and are not written to <c>config.json</c>. <c>--workspace-trust</c> overrides the bridge trust
/// check for this process. <c>--debug</c> logs protocol
/// traffic to stderr, or to a file when one is named. No option carries a secret.
/// </summary>
public sealed class BridgeSettings : SessionLaunchSettings
{
    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--space")]
    public bool Space { get; init; }

    [CommandOption("--config-options <IDS>")]
    public string? ConfigOptions { get; init; }

    [CommandOption("--default-config-options")]
    public bool DefaultConfigOptions { get; init; }

    [CommandOption("--all-config-options")]
    public bool AllConfigOptions { get; init; }

    [CommandOption("--workspace-trust <on|off>")]
    public string? WorkspaceTrust { get; init; }

    [CommandOption("--debug [FILE]")]
    public FlagValue<string?>? Debug { get; init; }
}
