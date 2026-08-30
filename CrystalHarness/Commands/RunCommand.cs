using Spectre.Console;
using Spectre.Console.Cli;

using CrystalHarness.Configuration;
using CrystalHarness.Home;

namespace CrystalHarness.Commands;

/// <summary>
/// Loads home settings and constructs the configured chat provider.
/// </summary>
public sealed class RunCommand : AsyncCommand<RunSettings>
{
    protected override Task<int> ExecuteAsync(
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
            return Task.FromResult(1);
        }

        var client = ChatClientFactory.Create(harnessSettings, apiKey);
        (client as IDisposable)?.Dispose();

        var workspace = ResolveWorkspace(settings.Workspace);
        var model = harnessSettings.ActiveModel;
        AnsiConsole.MarkupLine(
            $"[bold]Crystal[/]  {Markup.Escape(harnessSettings.Provider.Value)}  "
            + $"{Markup.Escape(harnessSettings.Model)}  "
            + $"{Markup.Escape(harnessSettings.Approval.Value)}");
        AnsiConsole.MarkupLine(
            $"[grey]{Markup.Escape(workspace)}  ctx {model.ContextWindow}[/]");
        return Task.FromResult(0);
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
