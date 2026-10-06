using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Shared options for <c>crystal plugins</c> and <c>crystal tools</c>.
/// These commands edit one manifest. They do not write <c>config.json</c>.
/// </summary>
public class ExtensionSettings : CommandSettings
{
    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--source <home|project>")]
    public string? Source { get; init; }

    [CommandOption("--format <text>")]
    public string? Format { get; init; }
}
