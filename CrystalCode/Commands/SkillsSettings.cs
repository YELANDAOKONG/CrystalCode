using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Options for <c>crystal skills</c>. Skills are discovered from several
/// trees, so this command only reads them and has no <c>--source</c>.
/// </summary>
public class SkillsSettings : CommandSettings
{
    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--format <text>")]
    public string? Format { get; init; }
}
