using Spectre.Console;
using Spectre.Console.Cli;

using CrystalHarness.Configuration;
using CrystalHarness.Home;
using CrystalHarness.Plugins;
using CrystalHarness.Sessions;

namespace CrystalHarness.Commands;

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
        var settingsStore = new SettingsStore(home);
        var harnessSettings = settingsStore
            .LoadOrCreate()
            .WithOverrides(settings.Provider, settings.Model);
        var credentials = new CredentialStore(home);
        if (!credentials.TryResolve(
                harnessSettings.ActiveProvider,
                out var apiKey,
                out var error))
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            return 1;
        }

        var workspace = ResolveWorkspace(settings.Workspace);
        var plugins = PluginRegistry.CreateBuiltIn();
        var client = ChatClientFactory.Create(harnessSettings, apiKey, plugins);
        try
        {
            var session = CodingSession.Create(
                client,
                harnessSettings,
                settingsStore,
                home,
                workspace,
                plugins);
            return await session.RunAsync(cancellationToken);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
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
