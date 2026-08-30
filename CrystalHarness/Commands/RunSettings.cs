using Spectre.Console.Cli;

namespace CrystalHarness.Commands;

/// <summary>
/// Options for the default interactive command.
/// </summary>
public sealed class RunSettings : CommandSettings
{
    [CommandOption("-p|--provider <PROVIDER>")]
    public string? Provider { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    public string? Model { get; init; }

    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }
}
