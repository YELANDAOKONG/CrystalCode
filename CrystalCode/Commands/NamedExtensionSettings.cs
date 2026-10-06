using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>One plugin or tool set directory.</summary>
public sealed class NamedExtensionSettings : ExtensionSettings
{
    [CommandArgument(0, "<DIRECTORY>")]
    public string Directory { get; init; } = string.Empty;
}
