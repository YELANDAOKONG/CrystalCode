using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Lists the skills found for the workspace.</summary>
public sealed class SkillsListCommand : AsyncCommand<SkillsSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        SkillsSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SkillsConsole.Run(settings, null));
    }
}
