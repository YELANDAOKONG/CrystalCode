using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Removes the saved login of an account provider.</summary>
public sealed class LogoutCommand : AsyncCommand<AccountSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        AccountSettings settings,
        CancellationToken cancellationToken) =>
        AccountConsole.LogoutAsync(settings, Console.Out, Console.Error, cancellationToken);
}
