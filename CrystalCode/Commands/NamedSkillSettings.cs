using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>One skill by name.</summary>
public sealed class NamedSkillSettings : SkillsSettings
{
    [CommandArgument(0, "<NAME>")]
    public string Name { get; init; } = string.Empty;
}
