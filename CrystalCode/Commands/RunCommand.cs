using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Run;
using CrystalCode.Terminal;
using Spectre.Console;
using Spectre.Console.Cli;

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

        var home = CrystalHome.Resolve(settings.Home);
        var settingsStore = new SettingsStore(home);
        var loaded = settingsStore.LoadOrCreate();
        if (!TaskRunOverrides.TryApply(loaded, settings, out var harnessSettings, out _, out var applyError))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(applyError)}[/]");
            return 1;
        }

        if (!TaskRunOverrides.TryAcceptPromptSet(home, settings.PromptSet, out var promptError))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(promptError)}[/]");
            return 1;
        }

        // Provider and model stay eligible for a later preference save.
        // The other launch flags stay on the live settings only.
        var persisted = loaded.WithOverrides(settings.Provider, settings.Model);
        bool? launchPlan = null;
        if (settings.Plan)
        {
            launchPlan = true;
        }
        else if (settings.Work)
        {
            launchPlan = false;
        }
        var credentials = new CredentialStore(home);
        if (!credentials.TryResolve(
                harnessSettings.ActiveProvider,
                out _,
                out var error))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var workspace = ResolveWorkspace(workspacePath);
        SessionDocument? resumed = null;
        if (resume.IsSet)
        {
            var sessions = new SessionStore(home);
            if (!ResumeRequest.TryParse(
                    resume.Value,
                    Environment.CurrentDirectory,
                    sessions,
                    out var request,
                    out var resumeError))
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                return 1;
            }

            switch (request.Target)
            {
                case ResumeRequest.Kind.Session:
                    if (!TryLoadResume(sessions, workspace, request.Value, out resumed, out resumeError))
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                        return 1;
                    }

                    break;
                case ResumeRequest.Kind.CurrentWorkspace:
                {
                    var picked = await PickResumeAsync(
                        sessions.List(workspace),
                        listWorkspace: false,
                        "No session for this workspace",
                        cancellationToken);
                    if (picked.ExitCode is int currentExit)
                    {
                        return currentExit;
                    }

                    if (!TryLoadResume(sessions, workspace, picked.Id, out resumed, out resumeError))
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                        return 1;
                    }

                    break;
                }
                case ResumeRequest.Kind.Workspace:
                {
                    if (!string.IsNullOrWhiteSpace(workspacePath)
                        && !ResumeRequest.SameDirectory(workspacePath, request.Value!))
                    {
                        AnsiConsole.MarkupLine("[red]Workspace and --resume path differ.[/]");
                        return 1;
                    }

                    workspace = request.Value!;
                    var namedTrust = await EnsureTrustedAsync(
                        home,
                        harnessSettings,
                        workspace,
                        cancellationToken);
                    if (!namedTrust.Ready)
                    {
                        return namedTrust.ExitCode;
                    }

                    var picked = await PickResumeAsync(
                        sessions.List(workspace),
                        listWorkspace: false,
                        "No session for this workspace",
                        cancellationToken);
                    if (picked.ExitCode is int namedExit)
                    {
                        return namedExit;
                    }

                    if (!TryLoadResume(sessions, workspace, picked.Id, out resumed, out resumeError))
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                        return 1;
                    }

                    break;
                }
                case ResumeRequest.Kind.AllWorkspaces:
                {
                    var picked = await PickResumeAsync(
                        sessions.List(workspaceRoot: null),
                        listWorkspace: true,
                        "No sessions",
                        cancellationToken);
                    if (picked.ExitCode is int allExit)
                    {
                        return allExit;
                    }

                    if (!TryLoadResume(sessions, workspace, picked.Id, out resumed, out resumeError))
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                        return 1;
                    }

                    if (!ResumeRequest.TryCanonicalWorkspace(
                            resumed.Workspace,
                            out workspace,
                            out resumeError))
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(resumeError)}[/]");
                        return 1;
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(settings),
                        request.Target,
                        "Resume target is not supported.");
            }
        }

        var trust = await EnsureTrustedAsync(
            home,
            harnessSettings,
            workspace,
            cancellationToken);
        if (!trust.Ready)
        {
            return trust.ExitCode;
        }

        var plugins = PluginRegistry.CreateBuiltIn();
        var host = TerminalHost.Create(
            harnessSettings,
            settingsStore,
            credentials,
            home,
            workspace,
            plugins,
            resumed,
            persisted,
            launchPlan);
        return await host.RunAsync(cancellationToken);
    }

    private static bool TryLoadResume(
        SessionStore sessions,
        string workspace,
        string? id,
        out SessionDocument document,
        out string error) =>
        SessionResume.TryLoad(sessions, workspace, id, out document, out error);

    private static async Task<(int? ExitCode, string? Id)> PickResumeAsync(
        IReadOnlyList<SessionSummary> available,
        bool listWorkspace,
        string emptyMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (available.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(emptyMessage)}[/]");
            return (1, null);
        }

        if (Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine(
                "[red]Interactive resume requires a terminal. Pass --resume <id>.[/]");
            return (1, null);
        }

        using var interrupt = TerminalInterrupt.Link(cancellationToken);
        using var pickerRenderer = new SessionRenderer();
        try
        {
            using (pickerRenderer.Open())
            {
                var id = await new SessionPicker(pickerRenderer).ChooseAsync(
                    available,
                    currentId: null,
                    listWorkspace,
                    interrupt.Token);
                if (id is null)
                {
                    return (0, null);
                }

                return (null, id);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (0, null);
        }
    }

    private readonly record struct TrustDecision(bool Ready, int ExitCode);

    private static async Task<TrustDecision> EnsureTrustedAsync(
        CrystalHome home,
        HarnessSettings harnessSettings,
        string workspace,
        CancellationToken cancellationToken)
    {
        if (!harnessSettings.WorkspaceTrust || !Directory.Exists(workspace))
        {
            return new TrustDecision(true, 0);
        }

        var root = new Workspace(workspace).Root;
        var trust = new WorkspaceTrustStore(home);
        var trustRoot = trust.TrustRoot(root);
        if (trust.Contains(root))
        {
            return new TrustDecision(true, 0);
        }

        if (Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[red]Trusting this directory requires a terminal.[/]");
            return new TrustDecision(false, 1);
        }

        using var interrupt = TerminalInterrupt.Link(cancellationToken);
        using var trustRenderer = new SessionRenderer();
        try
        {
            using (trustRenderer.Open())
            {
                var accepted = await new TrustPrompt(trustRenderer).ConfirmAsync(
                    new WorkspaceTrustRequest(root, trustRoot),
                    interrupt.Token);
                if (!accepted)
                {
                    return new TrustDecision(false, 0);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TrustDecision(false, 0);
        }

        trust.Remember(root);
        return new TrustDecision(true, 0);
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
