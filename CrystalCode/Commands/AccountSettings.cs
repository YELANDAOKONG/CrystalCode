using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal login</c> and <c>crystal logout</c>.
/// </summary>
public sealed class AccountSettings : CommandSettings
{
    [CommandArgument(0, "[provider]")]
    public string? Provider { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }
}
