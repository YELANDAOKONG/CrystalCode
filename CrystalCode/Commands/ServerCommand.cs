using System.Runtime.InteropServices;

using Spectre.Console.Cli;

using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Version;
using CrystalCode.Server.Hosting;

namespace CrystalCode.Commands;

/// <summary>
/// Starts the HTTP front end for one session in a workspace. Other front
/// ends connect to it; see docs/server.md.
/// </summary>
public sealed class ServerCommand : AsyncCommand<ServerSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        ServerSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return RunAsync(settings, Console.In, Console.Out, Console.Error, PluginRegistry.CreateBuiltIn(), cancellationToken);
    }

    internal static async Task<int> RunAsync(
        ServerSettings settings,
        TextReader input,
        TextWriter output,
        TextWriter error,
        PluginRegistry plugins,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var (token, generated, tokenError) = await ServerTokenSource.ResolveAsync(
            settings,
            input,
            Environment.GetEnvironmentVariable,
            cancellationToken);
        if (tokenError.Length > 0)
        {
            await error.WriteLineAsync(tokenError);
            return 1;
        }

        var options = new ServerOptions
        {
            Launch = new SessionLaunchRequest(
                settings.ToOptions(),
                Environment.CurrentDirectory,
                settings.Workspace,
                settings.Space,
                settings.Resume.IsSet,
                settings.Resume.Value),
            Listen = settings.Listen ?? ServerOptions.DefaultListen,
            Socket = settings.Socket,
            TlsCertificate = settings.TlsCert,
            TlsKey = settings.TlsKey,
            Token = token,
            TokenGenerated = generated,
            AllowedHosts = settings.AllowHost,
            AllowedOrigins = settings.AllowOrigin
        };
        var runtime = new ServerRuntime(
            output,
            error,
            plugins,
            BuildIdentityReader.Read(
                typeof(Program).Assembly,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture),
            ShowGeneratedToken: !Console.IsErrorRedirected);
        if (!CrystalServer.TryCreate(options, runtime, out var server, out var createError))
        {
            await error.WriteLineAsync(createError);
            return 1;
        }

        await using (server)
        {
            using var signals = ShutdownSignals.Register(server.Shutdown);
            using var cancel = cancellationToken.Register(() => server.Shutdown(ServerStopReason.Cancelled));
            try
            {
                await server.StartAsync(cancellationToken);
            }
            catch (IOException exception)
            {
                await error.WriteLineAsync("The server could not listen  " + exception.Message);
                return 1;
            }

            // A wrapper that closes stdin ends the server with it.
            using var watching = new CancellationTokenSource();
            var watch = settings.ExitOnStdinClose && !settings.TokenStdin
                ? WatchStdinAsync(input, server, watching.Token)
                : Task.CompletedTask;
            var code = await server.RunAsync(CancellationToken.None);
            await watching.CancelAsync();
            await watch;
            return code;
        }
    }

    private static async Task WatchStdinAsync(TextReader input, CrystalServer server, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new char[256];
            while (await input.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException)
        {
        }

        server.Shutdown(ServerStopReason.StdinClosed);
    }
}
