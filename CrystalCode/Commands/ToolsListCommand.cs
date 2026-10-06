using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Lists external tool set directories, including disabled ones.</summary>
public sealed class ToolsListCommand : AsyncCommand<ExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        ExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Tools(settings, null, "list"));
    }
}
