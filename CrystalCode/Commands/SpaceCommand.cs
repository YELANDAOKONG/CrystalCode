using Spectre.Console.Cli;

using CrystalCode.Engine.Home;

namespace CrystalCode.Commands;

/// <summary>
/// Creates the operator space when needed and opens the terminal there.
/// </summary>
public sealed class SpaceCommand : AsyncCommand<SpaceSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        SpaceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var home = CrystalHome.Resolve(settings.Home);
        var space = OperatorSpace.EnsureCreated(home);
        return RunCommand.RunAsync(
            new RunSettings
            {
                Provider = settings.Provider,
                Model = settings.Model,
                Home = settings.Home,
                Workspace = space,
                Resume = settings.Resume,
            },
            cancellationToken);
    }
}
