using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Terminal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Loads home settings and runs the interactive coding session.
/// </summary>
public sealed class RunCommand : AsyncCommand<RunSettings>
{
    public override async Task<int> ExecuteAsync(
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

        if (harnessSettings.WorkspaceTrust && Directory.Exists(workspace))
        {
            var root = new Workspace(workspace).Root;
            var trustRoot = GitRoot.TrustRoot(root);
            var trust = new WorkspaceTrustStore(home);
            if (!trust.Contains(trustRoot))
            {
                if (Console.IsInputRedirected)
                {
                    AnsiConsole.MarkupLine("[red]Trusting this directory requires a terminal.[/]");
                    return 1;
                }

                using var trustRenderer = new SessionRenderer();
                using (trustRenderer.Open())
                {
                    var accepted = await new TrustPrompt(trustRenderer).ConfirmAsync(
                        new WorkspaceTrustRequest(root, trustRoot),
                        cancellationToken);
                    if (!accepted)
                    {
                        return 0;
                    }
                }

                trust.Remember(trustRoot);
            }
        }

        var plugins = PluginRegistry.CreateBuiltIn();
        var host = TerminalHost.Create(
            harnessSettings,
            settingsStore,
            credentials,
            home,
            workspace,
            plugins,
            resume);
        return await host.RunAsync(cancellationToken);
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
