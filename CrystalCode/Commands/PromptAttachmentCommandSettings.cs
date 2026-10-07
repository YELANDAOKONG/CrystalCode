using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal prompt-attachments</c>. These commands edit
/// <c>prompt.json</c>. They do not write <c>config.json</c>.
/// </summary>
public class PromptAttachmentCommandSettings : CommandSettings
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
