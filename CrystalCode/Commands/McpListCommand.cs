using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Lists MCP servers, including disabled ones.</summary>
public sealed class McpListCommand : AsyncCommand<ExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        ExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(McpConsole.Run(settings, null, "list"));
    }
}
