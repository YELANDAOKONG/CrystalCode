using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Lists Home prompt sets, including disabled ones.</summary>
public sealed class PromptSetsListCommand : AsyncCommand<PromptSetCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        PromptSetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Sets(settings, null, "list"));
    }
}
