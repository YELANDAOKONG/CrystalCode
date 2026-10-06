using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Lists plugin directories, including disabled ones.</summary>
public sealed class PluginsListCommand : AsyncCommand<ExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        ExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Plugins(settings, null, "list"));
    }
}
