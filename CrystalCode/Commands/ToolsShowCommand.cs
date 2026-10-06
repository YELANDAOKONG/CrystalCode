using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Shows one external tool set manifest.</summary>
public sealed class ToolsShowCommand : AsyncCommand<NamedExtensionSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedExtensionSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionConsole.Tools(settings, settings.Directory, "show"));
    }
}
