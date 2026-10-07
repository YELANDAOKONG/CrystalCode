using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal space</c>. The workspace is the operator space.
/// </summary>
public sealed class SpaceSettings : SessionLaunchSettings
{
    [CommandOption("-r|--resume [ID]")]
    public required FlagValue<string?> Resume { get; init; }
}
