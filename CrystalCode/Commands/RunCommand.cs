using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Terminal;

namespace CrystalCode.Commands;

/// <summary>
/// Loads home settings and runs the interactive coding session.
/// </summary>
public sealed class RunCommand : AsyncCommand<RunSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        RunSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RunAsync(settings, cancellationToken);
    }

    internal static Task<int> RunAsync(
        RunSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return RunAsync(settings, settings.Workspace, settings.Resume, cancellationToken);
    }

    internal static async Task<int> RunAsync(
        SessionLaunchSettings settings,
        string? workspacePath,
        FlagValue<string?> resume,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        // Without a terminal there is nobody to ask, so the launcher reports
        // the question instead of drawing it.
        var prompt = Console.IsInputRedirected ? null : new StartupPrompt();
        var prepared = await SessionLauncher.PrepareAsync(
            new SessionLaunchRequest(
                settings.ToOptions(),
                Environment.CurrentDirectory,
                workspacePath,
                Resume: resume.IsSet,
                ResumeTarget: resume.Value),
            prompt,
            prompt,
            cancellationToken);
        if (prepared.Launch is not { } launch)
        {
            return Report(prepared);
        }

        TerminalHost host;
        try
        {
            host = TerminalHost.Create(launch, PluginRegistry.CreateBuiltIn());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
            return 1;
        }

        return await host.RunAsync(cancellationToken);
    }

    private static int Report(SessionLaunchResult result)
    {
        var message = result.Failure switch
        {
            SessionLaunchFailureKind.Declined => null,
            SessionLaunchFailureKind.TrustNeedsOperator => "Trusting this directory requires a terminal.",
            SessionLaunchFailureKind.ResumeNeedsOperator => "Interactive resume requires a terminal. Pass --resume <id>.",
            _ => result.Error
        };
        if (message is null)
        {
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        return 1;
    }
}
