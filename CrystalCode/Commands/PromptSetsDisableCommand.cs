using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Disables one prompt set.</summary>
public sealed class PromptSetsDisableCommand : AsyncCommand<NamedPromptSetCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedPromptSetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Sets(settings, settings.Directory, "disable"));
    }
}
