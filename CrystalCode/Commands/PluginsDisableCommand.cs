using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Disables one plugin directory.</summary>
public sealed class PluginsDisableCommand : AsyncCommand<NamedExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Plugins(settings, settings.Directory, "disable"));
    }
}
