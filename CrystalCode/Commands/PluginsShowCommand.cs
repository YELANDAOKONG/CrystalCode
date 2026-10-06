using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Shows one plugin manifest.</summary>
public sealed class PluginsShowCommand : AsyncCommand<NamedExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Plugins(settings, settings.Directory, "show"));
    }
}
