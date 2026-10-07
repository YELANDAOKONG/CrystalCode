using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Shows one prompt set manifest.</summary>
public sealed class PromptSetsShowCommand : AsyncCommand<NamedPromptSetCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedPromptSetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Sets(settings, settings.Directory, "show"));
    }
}
