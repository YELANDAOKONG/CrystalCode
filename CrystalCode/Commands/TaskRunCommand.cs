using CrystalCode.Run;

using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Runs one task without a terminal, then exits.
/// </summary>
public sealed class TaskRunCommand : AsyncCommand<TaskRunSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        TaskRunSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return TaskRunHost.ExecuteAsync(
            settings,
            Console.In,
            Console.Out,
            Console.Error,
            Console.IsInputRedirected,
            plugins: null,
            cancellationToken);
    }
}
