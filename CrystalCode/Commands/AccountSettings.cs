using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal login</c> and <c>crystal logout</c>. Without a
/// provider the commands list the account providers and their login state.
/// </summary>
public sealed class AccountSettings : CommandSettings
{
    [CommandArgument(0, "[provider]")]
    public string? Provider { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("--no-browser")]
    public bool NoBrowser { get; init; }

    [CommandOption("--format <text>")]
    public string? Format { get; init; }
}
