using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Signs in to an account provider in the terminal.</summary>
public sealed class LoginCommand : AsyncCommand<AccountSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        AccountSettings settings,
        CancellationToken cancellationToken) =>
        AccountConsole.LoginAsync(settings, Console.Out, Console.In, Console.Error, cancellationToken);
}
