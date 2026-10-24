using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>One MCP server name.</summary>
public sealed class NamedMcpSettings : ExtensionSettings
{
    [CommandArgument(0, "<SERVER>")]
    public string Server { get; init; } = string.Empty;
}
