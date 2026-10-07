using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Enables one prompt set and disables the others.</summary>
public sealed class PromptSetsEnableCommand : AsyncCommand<NamedPromptSetCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedPromptSetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Sets(settings, settings.Directory, "enable"));
    }
}
