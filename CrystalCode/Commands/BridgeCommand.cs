using System.Runtime.InteropServices;

using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Bridge.Hosting;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Version;

namespace CrystalCode.Commands;

/// <summary>
/// Runs the ACP Agent on stdin and stdout for an editor that started this
/// process. Stdout carries protocol lines only: the raw stream goes to
/// Bridge, and <see cref="Console.Out"/> and the Spectre console point at
/// stderr before anything else runs. See docs/bridge.md.
/// </summary>
public sealed class BridgeCommand : AsyncCommand<BridgeSettings>
{
    private static readonly PosixSignal[] StopSignals = [PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP];

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        BridgeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var output = Console.OpenStandardOutput();
        var input = Console.OpenStandardInput();
        ProtectStdout();
        return await RunAsync(settings, input, output, Console.Error, PluginRegistry.CreateBuiltIn(), cancellationToken);
    }

    /// <summary>
    /// Points every console writer at stderr, so a plugin, a tool, or a
    /// library that writes to the console cannot corrupt the protocol.
    /// </summary>
    internal static void ProtectStdout()
    {
        Console.SetOut(Console.Error);
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });
    }

    internal static async Task<int> RunAsync(
        BridgeSettings settings,
        Stream input,
        Stream output,
        TextWriter error,
        PluginRegistry plugins,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Space && !string.IsNullOrWhiteSpace(settings.Workspace))
        {
            await error.WriteLineAsync(SessionLauncher.SpaceWithWorkspace);
            return 1;
        }

        if (!TryConfigOptions(settings, out var configOptions, out var optionsError))
        {
            await error.WriteLineAsync(optionsError);
            return 1;
        }

        var options = new BridgeOptions(
            settings.ToOptions(),
            Environment.CurrentDirectory,
            plugins,
            Version(),
            error,
            settings.Workspace,
            settings.Space,
            configOptions);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var signals = ShutdownSignals.Register(StopSignals, stop.Cancel);
        await CrystalBridge.RunAsync(options, input, output, stop.Token);
        return 0;
    }

    /// <summary>
    /// The option ids a client sees. A flag wins; otherwise <c>config.json</c>
    /// applies while its custom switch is on; otherwise the built-in default.
    /// </summary>
    internal static bool TryConfigOptions(
        BridgeSettings settings,
        out IReadOnlyList<string> options,
        out string error)
    {
        options = BridgeHostSettings.DefaultOptions;
        error = string.Empty;
        var flags = (settings.ConfigOptions is not null ? 1 : 0)
            + (settings.DefaultConfigOptions ? 1 : 0)
            + (settings.AllConfigOptions ? 1 : 0);
        if (flags > 1)
        {
            error = "Use only one of --config-options, --default-config-options, and --all-config-options.";
            return false;
        }

        if (settings.DefaultConfigOptions)
        {
            return true;
        }

        if (settings.AllConfigOptions)
        {
            options = BridgeHostSettings.AvailableOptions;
            return true;
        }

        if (settings.ConfigOptions is not null)
        {
            if (!BridgeHostSettings.TryParse(settings.ConfigOptions, out var parsed, out error))
            {
                return false;
            }

            options = BridgeHostSettings.Expand(parsed);
            return true;
        }

        try
        {
            options = new DiskSettingsStore(CrystalHome.Resolve(settings.Home)).LoadOrCreate().Bridge.Effective;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error = "Could not read config.json  " + exception.Message;
            return false;
        }
    }

    /// <summary>The short product commit, or <c>dev</c> for a build without one.</summary>
    internal static string Version()
    {
        var revision = BuildIdentityReader.Read(typeof(Program).Assembly, RuntimeInformation.FrameworkDescription)
            .ProductRevision;
        return revision is null ? "dev" : revision[..12];
    }
}
