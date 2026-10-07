using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for the default interactive command.
/// </summary>
public sealed class RunSettings : SessionLaunchSettings
{
    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("-r|--resume [TARGET]")]
    public required FlagValue<string?> Resume { get; init; }
}
