using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Enables one plugin directory.</summary>
public sealed class PluginsEnableCommand : AsyncCommand<NamedExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Plugins(settings, settings.Directory, "enable"));
    }
}
