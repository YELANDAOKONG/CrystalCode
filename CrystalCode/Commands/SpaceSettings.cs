using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal space</c>. The workspace is the operator space.
/// </summary>
public sealed class SpaceSettings : CommandSettings
{
    [CommandOption("-p|--provider <PROVIDER>")]
    public string? Provider { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    public string? Model { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("-r|--resume [ID]")]
    public required FlagValue<string?> Resume { get; init; }
}
