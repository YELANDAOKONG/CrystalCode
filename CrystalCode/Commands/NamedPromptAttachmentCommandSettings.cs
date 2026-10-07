using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>One prompt-attachment directory.</summary>
public sealed class NamedPromptAttachmentCommandSettings : PromptAttachmentCommandSettings
{
    [CommandArgument(0, "<DIRECTORY>")]
    public string Directory { get; init; } = string.Empty;
}
