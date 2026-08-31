using CrystalCode.Home;
using CrystalCode.Plugins;
using CrystalCode.Sessions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Loads home settings and runs the interactive coding session.
/// </summary>
public sealed class RunCommand : AsyncCommand<RunSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        RunSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var home = CrystalHome.Resolve(settings.Home);
        var workspace = ResolveWorkspace(settings.Workspace);
        SessionDocument? resume = null;
        if (!string.IsNullOrWhiteSpace(settings.Resume))
        {
            var sessions = new SessionStore(home);
            if (!SessionResume.TryLoad(
                    sessions,
                    workspace,
                    settings.Resume,
                    out resume,
                    out var resumeError))
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                return 1;
            }
        }

        var settingsStore = new SettingsStore(home);
        var harnessSettings = settingsStore
            .LoadOrCreate()
            .WithOverrides(settings.Provider, settings.Model);
        var credentials = new CredentialStore(home);
        if (!credentials.TryResolve(
                harnessSettings.ActiveProvider,
                out _,
                out var error))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var plugins = PluginRegistry.CreateBuiltIn();
        var session = CodingSession.Create(
            harnessSettings,
            settingsStore,
            credentials,
            home,
            workspace,
            plugins,
            resume);
        return await session.RunAsync(cancellationToken);
    }

    private static string ResolveWorkspace(string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return Path.GetFullPath(Environment.CurrentDirectory);
        }

        return Path.GetFullPath(workspace);
    }
}
