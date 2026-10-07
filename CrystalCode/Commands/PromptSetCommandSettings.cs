using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal promptsets</c>. These commands edit
/// <c>prompt.json</c>. They do not write <c>config.json</c>.
/// </summary>
public class PromptSetCommandSettings : CommandSettings
{
    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("--format <text>")]
    public string? Format { get; init; }
}
