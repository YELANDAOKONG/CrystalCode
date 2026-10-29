using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Shows one skill.</summary>
public sealed class SkillsShowCommand : AsyncCommand<NamedSkillSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedSkillSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SkillsConsole.Run(settings, settings.Name));
    }
}
