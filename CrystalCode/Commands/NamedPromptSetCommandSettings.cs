using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>One Home prompt-set directory.</summary>
public sealed class NamedPromptSetCommandSettings : PromptSetCommandSettings
{
    [CommandArgument(0, "<DIRECTORY>")]
    public string Directory { get; init; } = string.Empty;
}
