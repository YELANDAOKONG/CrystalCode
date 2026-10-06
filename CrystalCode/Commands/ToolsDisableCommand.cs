using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Disables one external tool set directory.</summary>
public sealed class ToolsDisableCommand : AsyncCommand<NamedExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Tools(settings, settings.Directory, "disable"));
    }
}
