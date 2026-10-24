using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Enables one MCP server.</summary>
public sealed class McpEnableCommand : AsyncCommand<NamedMcpSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedMcpSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(McpConsole.Run(settings, settings.Server, "enable"));
    }
}
