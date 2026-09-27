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
        if (settings.Resume.IsSet)
        {
            var sessions = new SessionStore(home);
            var id = settings.Resume.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                var available = sessions.List(workspace);
                if (available.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]No session for this workspace[/]");
                    return 1;
                }

                if (Console.IsInputRedirected)
                {
                    AnsiConsole.MarkupLine(
                        "[red]Interactive resume requires a terminal. Pass --resume <id>.[/]");
                    return 1;
                }

                using var pickerRenderer = new SessionRenderer();
                using (pickerRenderer.Open())
                {
                    id = await new SessionPicker(pickerRenderer).ChooseAsync(
                        available,
                        currentId: null,
                        cancellationToken);
                }

                if (id is null)
                {
                    return 0;
                }
            }

            if (!SessionResume.TryLoad(
                    sessions,
                    workspace,
                    id,
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
